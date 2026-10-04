namespace PCBuilder.Models
{
    /// <summary>
    /// 需要加密保存的應用程式設定（例如 AI API Key）。Value 為 DPAPI（CurrentUser）加密後的內容。
    /// </summary>
    public class AppSettings
    {
        public int Id { get; set; }

        public required string Key { get; set; }

        public byte[] Value { get; set; } = [];
    }
}
