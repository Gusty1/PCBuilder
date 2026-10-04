

namespace PCBuilder.Models.DTOs
{
    public class MyMenuCategoryDTO
    {
        public int Id { get; set; }

        public required string Name { get; set; }

        public DateTime? ReviseDate { get; set; } = DateTime.Now;

        public bool IsSend { get; set; } = false;

        public string? HtmUrl { get; set; }

        public string? PngUrl { get; set; }

        public Dictionary<string, List<MenuProduct>> MyMenuProducts { get; set; } = [];

        public bool HasProducts => MyMenuProducts.Count > 0;

        public int TotalQty => MyMenuProducts.Sum(k => k.Value.Sum(p => p.Qty));

        public int TotalPrice => MyMenuProducts.Sum(k => k.Value.Sum(p => p.ProductPrice * p.Qty));

        /// <summary>菜單管理頁的卡片是否展開（只是畫面狀態，不存資料庫；預設收合）。</summary>
        public bool IsExpanded { get; set; }
    }
}
