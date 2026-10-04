using System.ComponentModel.DataAnnotations;

namespace PCBuilder.Models
{
    /// <summary>
    /// table Category 設定
    /// </summary>
    public class Category
    {
        [Key]
        public required int CategoryId { get; set; }

        public required string CategoryName { get; set; }

        public required string Summary { get; set; }

        public List<Subcategory>? Subcategories { get; set; } = [];
    }
}
