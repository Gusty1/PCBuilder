using System.ComponentModel.DataAnnotations;

namespace PCBuilder.Models
{
    /// <summary>
    /// table Product 設定
    /// </summary>
    public class Product
    {
        [Key]
        public int Id { get; set; }

        public required string SubcategoryName { get; set; }

        public required string Index { get; set; }

        public string? Group { get; set; }

        public int? Price { get; set; }

        public List<string>? Markers { get; set; } = [];

        public string? RawText { get; set; }

        public string? FullText { get; set; }

        public string? ImgUrl { get; set; }

        public string? ProductUrl { get; set; }

        public List<string>? Details { get; set; } = [];
    }
}
