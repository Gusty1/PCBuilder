using System.ComponentModel.DataAnnotations;

namespace PCBuilder.Models
{
    /// <summary>
    /// table MenuCategory 設定
    /// </summary>
    public class MenuCategory
    {
        [Key]
        public int Id { get; set; }

        public required string Name { get; set; }

        public DateTime? ReviseDate { get; set; } = DateTime.Now;

        public bool IsSend { get; set; } = false;

        public string? HtmUrl { get; set; }

        public string? PngUrl { get; set; }

        public List<MenuProduct>? MenuProducts { get; set; } = [];
    }
}
