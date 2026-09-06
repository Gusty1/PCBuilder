using System.Text.Json.Serialization;

namespace PCBuilder.Models.DTOs
{
    /// <summary>
    /// 獲取json檔案中的子目錄資料
    /// </summary>
    public class SubcategoryDTO
    {
        [JsonPropertyName("name")]
        public required string Name { get; set; }

        [JsonPropertyName("products")]
        public List<ProductDTO>? Products { get; set; }
    }
}
