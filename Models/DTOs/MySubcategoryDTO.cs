

namespace PCBuilder.Models.DTOs
{
    /// <summary>
    /// 首頁我的子目錄資料
    /// </summary>
    public class MySubcategoryDTO
    {
        public required int CategoryId { get; set; }

        public required string SubcategoryName { get; set; }
        
        //記錄我的子目錄產品數量
        public int Qty { get; set; }

        //有數量的產品
        public List<MyProductDTO>? Products { get; set; } = [];
    }
}
