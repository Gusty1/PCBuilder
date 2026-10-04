

namespace PCBuilder.Models.DTOs
{
    /// <summary>
    /// 首頁我的主目錄資料
    /// </summary>
    public class MyCategoryDTO
    {
        public required int CategoryId { get; set; }
        public required string CategoryName { get; set; }

        public required string Summary { get; set; }

        //我的子目錄List
        public List<MySubcategoryDTO>? Subcategories { get; set; } = [];

        public override string ToString() => CategoryName;
    }
}
