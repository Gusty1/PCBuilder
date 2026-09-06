

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
    }
}
