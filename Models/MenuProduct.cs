using System.ComponentModel.DataAnnotations;

namespace PCBuilder.Models
{
    /// <summary>
    /// table MenuProduct 設定
    /// </summary>
    public class MenuProduct
    {
        [Key]
        public int Id { get; set; }

        public int MenuCategoryId { get; set; }

        public int CategoryId { get; set; }

        public required string CategoryName { get; set; }

        public required string SubcategoryName { get; set; }

        public required string ProductName { get; set; }

        public required string ProductFullText { get; set; }

        public string? ProductUrl { get; set; }

        public int ProductPrice { get; set; }

        public int Qty { get; set; }
    }
}
