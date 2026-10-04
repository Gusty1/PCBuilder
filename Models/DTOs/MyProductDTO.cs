using CommunityToolkit.Mvvm.ComponentModel;

namespace PCBuilder.Models.DTOs
{
    /// <summary>
    /// 首頁我的商品資料
    /// </summary>
    public partial class MyProductDTO : ObservableObject
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

        public string? DetailsText => Details is { Count: > 0 } ? string.Join("　/　", Details) : null;

        //記錄我目前的商品數量
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsInMenu))]
        private int qty;

        public bool IsInMenu => Qty > 0;

        /// <summary>是否為熱銷商品（Markers 含 "hot"，不分大小寫）。</summary>
        public bool IsHot => Markers?.Any(m => m.Contains("hot", StringComparison.OrdinalIgnoreCase)) ?? false;
    }
}
