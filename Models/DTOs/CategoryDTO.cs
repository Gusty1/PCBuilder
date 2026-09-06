using System.Text.Json.Serialization;

namespace PCBuilder.Models.DTOs
{
    /// <summary>
    /// 獲取json檔案中的主目錄資料
    /// </summary>
    public class CategoryDTO
    {
        [JsonPropertyName("category_id")]
        public required string CategoryId { get; set; }

        [JsonPropertyName("category_name")]
        public required string CategoryName { get; set; }

        [JsonPropertyName("summary")]
        public required string Summary { get; set; }

        [JsonPropertyName("subcategories")]
        public List<SubcategoryDTO>? Subcategories { get; set; }
    }
}
