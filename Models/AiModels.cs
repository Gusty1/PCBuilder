using CommunityToolkit.Mvvm.ComponentModel;
using PCBuilder.Models.DTOs;

namespace PCBuilder.Models
{
    /// <summary>送給 AI 的一則對話；Role 使用 Gemini 的角色名稱 "user" / "model"。</summary>
    public record ChatMessage(string Role, string Content);

    /// <summary>AI 回覆：去掉推薦 JSON 區塊後的文字，以及已對應到實際商品的推薦清單。</summary>
    public record AiReply(string Text, List<AiRecommendation> Recommendations);

    /// <summary>存檔用的 AI 對話：History 是送給 AI 的上下文，Messages 是面板上顯示的訊息（含錯誤訊息）。</summary>
    public record SavedChat(List<ChatMessage> History, List<SavedChatMessage> Messages);

    public record SavedChatMessage(bool IsUser, string Text, bool IsError, List<SavedRecommendation> Recommendations);

    /// <summary>推薦商品只存商品名稱，重開時對照最新的商品資料（價格才是最新的）。</summary>
    public record SavedRecommendation(string ProductName, string Reason, int Qty, bool IsAdded);

    /// <summary>AI 推薦且已在商品資料中找到對應的項目。</summary>
    public partial class AiRecommendation(MyCategoryDTO category, MyProductDTO product, string reason, int qty) : ObservableObject
    {
        public MyCategoryDTO Category { get; } = category;

        public MyProductDTO Product { get; } = product;

        public string Reason { get; } = reason;

        public int Qty { get; } = qty;

        [ObservableProperty]
        private bool isAdded;
    }
}
