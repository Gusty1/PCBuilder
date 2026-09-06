

namespace PCBuilder.Models.DTOs
{
    /// <summary>
    /// 首頁我的商品資料
    /// </summary>
    public class MyProductDTO
    {
        public required string Index { get; set; }

        public required string SubcategoryName { get; set; }

        public string? Group { get; set; }

        public int? Price { get; set; }

        public List<string>? Markers { get; set; } = [];

        public string? RawText { get; set; }

        public string? FullText { get; set; }

        public string? ImgUrl { get; set; }

        public string? ProductUrl { get; set; }

        public List<string>? Details { get; set; } = [];

        //記錄我目前的商品數量
        public int Qty { get; set; }

        /// <summary>是否為熱銷商品（Markers 含 "hot"，不分大小寫）。</summary>
        public bool IsHot => Markers?.Any(m => m.Contains("hot", StringComparison.OrdinalIgnoreCase)) ?? false;
    }
}
