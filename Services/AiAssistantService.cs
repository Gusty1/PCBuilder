using PCBuilder.Models;
using PCBuilder.Models.DTOs;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PCBuilder.Services
{
    /// <summary>AI 挑中的子分類；Label（主分類 > 子分類）同時是給 AI 挑選的名稱與 prompt 中的段落標題。</summary>
    internal sealed record PickedSubcategory(string CategoryName, MySubcategoryDTO Subcategory)
    {
        public string Label => $"{CategoryName} > {Subcategory.SubcategoryName}";
    }

    /// <summary>
    /// AI 助手：組合「本機硬體 + 目標菜單 + AI 挑選的商品子分類」作為 context 送給 Gemini，
    /// 再把回覆中的推薦 JSON 對應回實際商品，供介面一鍵加入菜單。
    /// </summary>
    public partial class AiAssistantService(GeminiAiService gemini, CategoryService categoryService, HardwareService hardwareService)
    {
        // 組一整台電腦約 8 種零件、每種挑 1～2 個子分類；簡單問題 AI 仍只會挑 1～2 個
        private const int MaxContextSubcategories = 12;
        private const int CategoryPickHistoryCount = 4;
        private const int MaxSpecLength = 120;
        // 商品清單總長度的硬上限（字元，約 3～5 萬 token），避免超過免費層每分鐘 input token 額度；
        // 可對照 Debug 輸出的 promptTokenCount 調整
        private const int MaxProductContextChars = 80_000;

        private sealed record ProductLine(MyProductDTO Product, string Line);

        public async Task<AiReply> ChatAsync(IReadOnlyList<ChatMessage> history, MenuCategory? targetMenu,
            CancellationToken cancellationToken = default)
        {
            var categories = await categoryService.GetCategoriesWithDetailsAsync(null!);
            var (picked, maxPrice) = await PickSubcategoriesAsync(history, categories, cancellationToken);

            var raw = await gemini.GenerateAsync(BuildSystemPrompt(targetMenu, picked, maxPrice), history, cancellationToken: cancellationToken);
            return ParseReply(raw, categories);
        }

        /// <summary>
        /// 依商品名稱對照目前的商品資料（價格是最新的），用來還原上次對話的推薦商品；
        /// 回傳的字典只包含還找得到的商品，原價屋已下架的不在裡面。
        /// </summary>
        public async Task<Dictionary<string, (MyCategoryDTO Category, MyProductDTO Product)>> FindProductsAsync(IEnumerable<string> names)
        {
            var distinct = names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
            if (distinct.Count == 0) return [];

            var categories = await categoryService.GetCategoriesWithDetailsAsync(null!);
            var found = new Dictionary<string, (MyCategoryDTO Category, MyProductDTO Product)>();
            foreach (var name in distinct)
                if (FindProduct(categories, name) is { } product)
                    found[name] = product;
            return found;
        }

        // 第一段呼叫只給「主分類 > 子分類」名稱，讓 AI 判斷這次需要哪些子分類的商品與預算上限，
        // 避免把 6000 多項商品全部塞進 prompt
        private async Task<(List<PickedSubcategory> Picked, int? MaxPrice)> PickSubcategoriesAsync(
            IReadOnlyList<ChatMessage> history, List<MyCategoryDTO> categories, CancellationToken cancellationToken)
        {
            var all = categories
                .SelectMany(c => (c.Subcategories ?? []).Select(s => new PickedSubcategory(c.CategoryName, s)))
                .ToList();

            var prompt = $$"""
                你負責判斷使用者的問題需要參考原價屋的哪些商品子分類，以及預算上限。
                可選的子分類（格式：主分類 > 子分類）：
                {{string.Join("\n", all.Select(s => s.Label))}}

                請根據對話內容挑出最相關的子分類（最多 {{MaxContextSubcategories}} 個），每項必須逐字複製上面清單中的一整行。
                如果使用者要組一整台電腦，請挑齊組裝需要的零件（例如處理器、主機板、記憶體、硬碟、顯示卡、電源、機殼、散熱器），每種零件挑最符合需求的 1～2 個子分類。
                maxPrice 填使用者提到的預算上限（新台幣整數）；沒有提到預算填 null。
                只輸出 JSON，例如 {"subcategories": ["{{all.FirstOrDefault()?.Label}}"], "maxPrice": 30000}；若問題與商品無關，subcategories 輸出 []。
                """;

            // Gemini 要求對話以 user 開頭並交替，截取最近幾則時要跳過開頭的 model 訊息
            var recent = history.TakeLast(CategoryPickHistoryCount).SkipWhile(m => m.Role != "user").ToList();
            var json = await gemini.GenerateAsync(prompt, recent, asJson: true, cancellationToken: cancellationToken);
            return ParsePick(json, all);
        }

        internal static (List<PickedSubcategory> Picked, int? MaxPrice) ParsePick(string json, List<PickedSubcategory> all)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return ([], null);

                // AI 偶爾會改動「>」兩側的空白，比對時忽略空白；只回子分類名稱時也接受
                var names = root.TryGetProperty("subcategories", out var subs) && subs.ValueKind == JsonValueKind.Array
                    ? subs.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => RemoveWhitespace(e.GetString()!)).ToHashSet()
                    : [];
                var picked = all
                    .Where(s => names.Contains(RemoveWhitespace(s.Label)) || names.Contains(RemoveWhitespace(s.Subcategory.SubcategoryName)))
                    .Take(MaxContextSubcategories)
                    .ToList();

                int? maxPrice = root.TryGetProperty("maxPrice", out var price) && price.ValueKind == JsonValueKind.Number
                                && price.TryGetDecimal(out var value) && value is > 0 and <= int.MaxValue
                    ? (int)value
                    : null;
                return (picked, maxPrice);
            }
            catch (JsonException)
            {
                return ([], null);
            }
        }

        private static string RemoveWhitespace(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));

        private string BuildSystemPrompt(MenuCategory? targetMenu, List<PickedSubcategory> picked, int? maxPrice)
        {
            var sb = new StringBuilder();
            sb.AppendLine("你是「組電腦小幫手」的 AI 助手，專門根據原價屋（coolpc.com.tw）的商品資料提供電腦組裝與採購建議。");
            sb.AppendLine("請用繁體中文、純文字回覆，不要使用 Markdown 語法（例如 **粗體**、# 標題、表格），價格以新台幣表示。");

            sb.AppendLine().AppendLine("【用戶本機硬體】").AppendLine(BuildHardwareSummary());

            sb.AppendLine();
            if (targetMenu is null)
            {
                sb.AppendLine("【目前菜單】尚未選擇菜單");
            }
            else
            {
                sb.AppendLine($"【目前菜單】（菜單名稱：{targetMenu.Name}）格式：分類 | 商品名稱 | 數量 | 單價");
                var items = targetMenu.MenuProducts ?? [];
                sb.AppendLine(items.Count == 0
                    ? "（空）"
                    : string.Join("\n", items.Select(p => $"{p.CategoryName} | {p.ProductName} | {p.Qty} | ${p.ProductPrice:N0}")));
            }

            var (products, truncated) = BuildProductContext(picked, maxPrice, MaxProductContextChars);
            sb.Append(products);
            Debug.WriteLine($"[AI] 子分類 {picked.Count} 個：{string.Join("、", picked.Select(p => p.Label))}；" +
                            $"maxPrice={maxPrice?.ToString() ?? "無"}；商品清單 {products.Length:N0} 字元{(truncated ? "（已截斷）" : "")}");

            if (picked.Count == 0)
                sb.AppendLine().AppendLine("（這次沒有帶入商品清單；若需要推薦商品，請請使用者說明想找哪一類商品。）");
            if (maxPrice is not null)
                sb.AppendLine().AppendLine($"（商品清單已依使用者預算排除單價超過 ${maxPrice:N0} 的商品。）");
            if (truncated)
                sb.AppendLine().AppendLine("（注意：商品清單已截斷，每個子分類只列出熱銷商品與各價位的代表商品；" +
                                           "若清單中找不到合適的商品，請建議使用者把問題縮小到特定零件、品牌或價位後再問一次。）");

            sb.AppendLine().AppendLine("""
                只能推薦上面「可用商品」清單中的商品，商品名稱必須逐字複製清單裡的名稱。
                如果你要推薦商品，請在回覆最後附上 JSON 區塊：
                ```json
                {"recommendations": [{"name": "商品完整名稱", "category": "分類", "reason": "推薦理由", "qty": 1}]}
                ```
                若不需要推薦商品則省略 JSON 區塊。
                你無法直接修改使用者的菜單：推薦商品後，請提醒使用者按推薦商品下方的「加入菜單」或「全部加入目標菜單」按鈕，不要說你已經幫忙加入。
                如果使用者有給預算，推薦組合的合計金額（單價 × 數量加總）不可超過預算，並在回覆中寫出合計金額。
                """);
            return sb.ToString();
        }

        private string BuildHardwareSummary()
        {
            // HardwareService 在背景掃描，尚未完成時 CurrentComputerInfo 會是 null
            if (hardwareService.CurrentComputerInfo is not { } info) return "（尚未取得硬體資訊）";

            var lines = new List<string>
            {
                $"主機板：{info.MotherboardManufacturer} {info.MotherboardProduct}",
                $"CPU：{info.CpuName}（{info.CpuCores} 核 / {info.CpuThreads} 緒）",
                $"RAM：共 {info.TotalPhysicalMemoryGb:0.#} GB"
                    + (info.RamSticks is { Count: > 0 } sticks
                        ? $"（{string.Join("、", sticks.Select(r => $"{r.CapacityGb:0.#}GB {r.SpeedMhz}MHz"))}）"
                        : ""),
            };
            lines.AddRange((info.Gpus ?? []).Select(g => $"GPU：{g.Name}（{g.AdapterRamGb:0.#} GB）"));
            lines.AddRange((info.Disks ?? []).Select(d => $"硬碟：{d.Model}（{d.SizeGb:0} GB）"));
            return string.Join("\n", lines);
        }

        /// <summary>
        /// 產生各子分類的「可用商品」段落。有預算時排除單價超過預算（或沒有價格）的商品；
        /// 總長度超過 maxChars 時每個子分類平均分配長度，熱銷商品優先，其餘依價格排序後均勻取樣，讓低中高價位都有代表。
        /// </summary>
        internal static (string Text, bool Truncated) BuildProductContext(IReadOnlyList<PickedSubcategory> picked, int? maxPrice, int maxChars)
        {
            var groups = picked
                .Select(s => (s.Label, Lines: (s.Subcategory.Products ?? [])
                    .Where(p => p.RawText is not null && (maxPrice is null || p.Price <= maxPrice))
                    .Select(p => new ProductLine(p, $"{p.RawText} | ${p.Price:N0} | {Truncate(p.DetailsText ?? p.Group)}"))
                    .ToList()))
                .Where(g => g.Lines.Count > 0)
                .ToList();

            var truncated = groups.Sum(g => Length(g.Lines)) > maxChars;
            var selected = truncated ? AllocateEvenly(groups.Select(g => g.Lines).ToList(), maxChars) : groups.Select(g => g.Lines).ToList();

            var sb = new StringBuilder();
            for (var i = 0; i < groups.Count; i++)
            {
                var note = selected[i].Count < groups[i].Lines.Count ? $"，已截斷：列出 {selected[i].Count} / {groups[i].Lines.Count} 項" : "";
                sb.AppendLine().AppendLine($"【可用商品（{groups[i].Label}{note}）】格式：商品名稱 | 價格 | 規格");
                foreach (var line in selected[i]) sb.AppendLine(line.Line);
            }
            return (sb.ToString(), truncated);
        }

        // 由小到大分配：每個子分類拿「剩餘額度 ÷ 剩餘子分類數」，小的子分類用不完的額度留給後面較大的子分類
        private static List<List<ProductLine>> AllocateEvenly(List<List<ProductLine>> groups, int maxChars)
        {
            var result = new List<ProductLine>[groups.Count];
            var order = Enumerable.Range(0, groups.Count).OrderBy(i => Length(groups[i])).ToList();
            var remaining = maxChars;
            for (var n = 0; n < order.Count; n++)
            {
                var i = order[n];
                result[i] = SelectWithin(groups[i], remaining / (order.Count - n));
                remaining -= Length(result[i]);
            }
            return [.. result];
        }

        // 熱銷優先，剩下的額度給其他商品；結果依價格排序
        private static List<ProductLine> SelectWithin(List<ProductLine> lines, int budget)
        {
            if (Length(lines) <= budget) return lines;

            var hot = SampleEvenly(lines.Where(l => l.Product.IsHot).OrderBy(l => l.Product.Price).ToList(), budget);
            var others = SampleEvenly(lines.Where(l => !l.Product.IsHot).OrderBy(l => l.Product.Price).ToList(), budget - Length(hot));
            return [.. hot.Concat(others).OrderBy(l => l.Product.Price)];
        }

        // 已依價格排序的清單，從最便宜到最貴等間隔挑 n 項；n 先用平均長度估算，放不下就逐一減少
        private static List<ProductLine> SampleEvenly(List<ProductLine> sorted, int budget)
        {
            if (sorted.Count == 0 || budget <= 0) return [];
            if (Length(sorted) <= budget) return sorted;

            for (var n = (int)Math.Min(sorted.Count, (long)budget * sorted.Count / Length(sorted)); n > 0; n--)
            {
                var sample = Enumerable.Range(0, n)
                    .Select(k => sorted[n == 1 ? sorted.Count / 2 : (int)Math.Round(k * (sorted.Count - 1.0) / (n - 1))])
                    .ToList();
                if (Length(sample) <= budget) return sample;
            }
            return [];
        }

        private static int Length(IEnumerable<ProductLine> lines) => lines.Sum(l => l.Line.Length + 1);

        private static string Truncate(string? text) =>
            text is null ? "" : text.Length <= MaxSpecLength ? text : text[..MaxSpecLength] + "…";

        [GeneratedRegex(@"```json\s*(\{[\s\S]*?\})\s*```", RegexOptions.IgnoreCase)]
        private static partial Regex JsonBlockRegex();

        private static AiReply ParseReply(string raw, List<MyCategoryDTO> categories)
        {
            var match = JsonBlockRegex().Match(raw);
            if (!match.Success) return new AiReply(raw.Trim(), []);

            var text = raw.Remove(match.Index, match.Length).Trim();
            var recommendations = new List<AiRecommendation>();
            try
            {
                using var doc = JsonDocument.Parse(match.Groups[1].Value);
                if (doc.RootElement.TryGetProperty("recommendations", out var recs) && recs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var rec in recs.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object))
                    {
                        var name = GetString(rec, "name");
                        if (string.IsNullOrWhiteSpace(name) || FindProduct(categories, name) is not { } found) continue;
                        if (recommendations.Any(r => ReferenceEquals(r.Product, found.Product))) continue;

                        var qty = rec.TryGetProperty("qty", out var q) && q.ValueKind == JsonValueKind.Number
                                  && q.TryGetInt32(out var value) && value > 0 ? value : 1;
                        recommendations.Add(new AiRecommendation(found.Category, found.Product, GetString(rec, "reason") ?? "", qty));
                    }
                }
            }
            catch (JsonException)
            {
                // 推薦 JSON 格式錯誤時只顯示文字回覆
            }

            return new AiReply(text, recommendations);
        }

        private static string? GetString(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        // AI 偶爾會少抄或多抄幾個字，完全相符找不到時退而用「包含」比對
        private static (MyCategoryDTO Category, MyProductDTO Product)? FindProduct(List<MyCategoryDTO> categories, string name)
        {
            var all = categories.SelectMany(c => (c.Subcategories ?? [])
                .SelectMany(s => s.Products ?? [])
                .Select(p => (Category: c, Product: p)))
                .Where(x => x.Product.RawText is not null)
                .ToList();

            var trimmed = name.Trim();
            foreach (var x in all)
                if (x.Product.RawText == trimmed) return x;
            foreach (var x in all)
                if (x.Product.RawText!.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
                    || trimmed.Contains(x.Product.RawText, StringComparison.OrdinalIgnoreCase))
                    return x;
            return null;
        }
    }
}
