using PCBuilder.Models;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PCBuilder.Services
{
    /// <summary>AI 服務的使用者可讀錯誤（訊息會直接顯示在對話框中）。</summary>
    public class AiServiceException(string message) : Exception(message);

    /// <summary>
    /// Gemini REST API 供應商實作。API Key 以 x-goog-api-key 標頭傳送，不放在網址上，避免出現在記錄中。
    /// </summary>
    public partial class GeminiAiService(IHttpClientFactory httpClientFactory, AppSettingsService settingsService)
    {
        public const string HttpClientName = "Gemini";
        public const string Provider = "Gemini";
        public const string Model = "gemini-3.8-flash";
        private const string ModelUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}";
        private const string ApiKeyHeader = "x-goog-api-key";

        /// <summary>用指定的 Key 查詢模型資訊，確認 Key 有效且模型可用（不會消耗生成額度）。</summary>
        public async Task<(bool Success, string Message)> TestApiKeyAsync(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) return (false, "請先輸入 API Key");

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, ModelUrl);
                request.Headers.Add(ApiKeyHeader, apiKey.Trim());
                using var response = await CreateClient().SendAsync(request);
                if (!response.IsSuccessStatusCode) return (false, await DescribeErrorAsync(response));

                var json = await response.Content.ReadFromJsonAsync<JsonObject>();
                var displayName = json?["displayName"]?.GetValue<string>() ?? Model;
                return (true, $"連線正常（{displayName}）");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return (false, "無法連線到 Gemini，請確認網路");
            }
        }

        /// <summary>送出對話並回傳模型輸出的文字；asJson 為 true 時要求模型只輸出 JSON。</summary>
        public async Task<string> GenerateAsync(string systemPrompt, IEnumerable<ChatMessage> history, bool asJson = false,
            CancellationToken cancellationToken = default)
        {
            var apiKey = await settingsService.GetSecretAsync(AppSettingsService.GeminiApiKey);
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new AiServiceException("尚未設定 Gemini API Key，請先到「設定」頁輸入並儲存");

            var body = new JsonObject
            {
                ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = systemPrompt }) },
                ["contents"] = new JsonArray(history.Select(m => (JsonNode)new JsonObject
                {
                    ["role"] = m.Role,
                    ["parts"] = new JsonArray(new JsonObject { ["text"] = m.Content }),
                }).ToArray()),
            };
            if (asJson)
                body["generationConfig"] = new JsonObject { ["responseMimeType"] = "application/json" };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ModelUrl}:generateContent")
            {
                Content = JsonContent.Create(body),
            };
            request.Headers.Add(ApiKeyHeader, apiKey);

            HttpResponseMessage response;
            try
            {
                response = await CreateClient().SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException)
            {
                throw new AiServiceException("無法連線到 Gemini，請確認網路");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new AiServiceException("Gemini 回應逾時，請稍後再試");
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                    throw new AiServiceException(await DescribeErrorAsync(response));

                var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
                // 實測每題實際用掉多少 token，用來調整 AiAssistantService 的商品清單長度上限
                var usage = json?["usageMetadata"];
                Debug.WriteLine($"[Gemini] promptTokenCount={usage?["promptTokenCount"]}，totalTokenCount={usage?["totalTokenCount"]}，" +
                                $"system prompt {systemPrompt.Length:N0} 字元");

                var parts = json?["candidates"]?[0]?["content"]?["parts"]?.AsArray();
                var text = parts is null ? null : string.Concat(parts.Select(p => p?["text"]?.GetValue<string>()));
                if (!string.IsNullOrWhiteSpace(text)) return text;

                var reason = json?["promptFeedback"]?["blockReason"]?.GetValue<string>()
                             ?? json?["candidates"]?[0]?["finishReason"]?.GetValue<string>();
                throw new AiServiceException(reason is null ? "Gemini 沒有回覆內容" : $"Gemini 沒有回覆內容（{reason}）");
            }
        }

        private HttpClient CreateClient() => httpClientFactory.CreateClient(HttpClientName);

        private static async Task<string> DescribeErrorAsync(HttpResponseMessage response) =>
            DescribeError(response.StatusCode, await response.Content.ReadAsStringAsync());

        private const string TooLargeAdvice = "請把問題縮小範圍（例如一次只問幾種零件，或指定品牌、價位）";

        /// <summary>
        /// 把 Gemini 錯誤回應轉成使用者看得懂的訊息。429 依 error.details 的 QuotaFailure（quotaId / quotaMetric）區分：
        /// 每分鐘 input token 超量 → 請求太大；每日額度、每分鐘請求數 → 額度用完，請稍後再試（附 RetryInfo 的秒數）。
        /// </summary>
        internal static string DescribeError(HttpStatusCode status, string? body)
        {
            string? detail = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(body) && JsonNode.Parse(body) is JsonObject root
                    && root["error"]?["message"] is JsonValue message && message.TryGetValue<string>(out var text))
                    detail = text;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                // 錯誤內容不是預期的 JSON，只用狀態碼判斷
            }

            // quotaId / quotaMetric / retryDelay 直接在原始內容中比對，不依賴 details 的完整結構
            var raw = body ?? "";
            var retry = RetryDelayRegex().Match(raw);
            var when = retry.Success ? $"約 {retry.Groups[1].Value} 秒後" : "稍後";

            return status switch
            {
                HttpStatusCode.BadRequest when Has(detail, "API key") => "API Key 無效",
                HttpStatusCode.BadRequest when Has(detail, "token") && Has(detail, "exceed") =>
                    $"請求太大：這次要送給 AI 的商品資料超過模型一次可處理的長度。{TooLargeAdvice}",
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "API Key 無效或沒有使用權限",
                HttpStatusCode.TooManyRequests when Has(raw, "PerDay") => "今天的 Gemini 免費額度已用完，請明天再試",
                HttpStatusCode.TooManyRequests when Has(raw, "input_token") || Has(raw, "InputToken") =>
                    $"請求太大：這次要送給 AI 的商品資料超過免費額度每分鐘的 token 上限。{TooLargeAdvice}；若剛剛連續提問，也可以等一分鐘後再試",
                HttpStatusCode.TooManyRequests => $"Gemini 額度已用完或請求太頻繁，請{when}再試",
                HttpStatusCode.NotFound => $"找不到模型 {Model}",
                _ => detail is null ? $"Gemini 錯誤（{(int)status}）" : $"Gemini 錯誤（{(int)status}）：{detail}",
            };
        }

        private static bool Has(string? text, string value) => text?.Contains(value, StringComparison.OrdinalIgnoreCase) == true;

        [GeneratedRegex(@"""retryDelay""\s*:\s*""(\d+)(?:\.\d+)?s""")]
        private static partial Regex RetryDelayRegex();
    }
}
