using System.Text.Json.Serialization;

namespace PCBuilder.Models.DTOs
{
    /// <summary>
    /// 獲取json檔案中的商品資料
    /// </summary>
    public class ProductDTO
    {
        [JsonPropertyName("index")]
        public required string Index { get; set; }

        [JsonPropertyName("group")]
        public string? Group { get; set; }

        [JsonPropertyName("price")]
        public int? Price { get; set; }

        [JsonPropertyName("discount_amount")]
        public int? Discount { get; set; }

        [JsonPropertyName("markers")]
        public List<string>? Markers { get; set; }

        [JsonPropertyName("raw_text")]
        public string? RawText { get; set; }

        [JsonPropertyName("full_text")]
        public string? FullText { get; set; }

        [JsonPropertyName("img_url")]
        public string? ImgUrl { get; set; }

        [JsonPropertyName("product_url")]
        public string? ProductUrl { get; set; }

        [JsonPropertyName("details")]
        public List<string>? Details { get; set; }
    }
}
