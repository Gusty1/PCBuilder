namespace PCBuilder.Models.DTOs
{
    /// <summary>
    /// 首頁我的子目錄資料
    /// </summary>
    public class MySubcategoryDTO
    {
        public required int CategoryId { get; set; }

        public required string SubcategoryName { get; set; }

        public List<MyProductDTO>? Products { get; set; } = [];

        // 子分類 Tab 上的徽章：該分類共有幾樣商品
        public int ProductCount => Products?.Count ?? 0;
    }
}
