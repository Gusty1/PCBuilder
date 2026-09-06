using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace PCBuilder.Services
{
    /// <summary>
    /// 對話框服務實作，使用 WPF UI 的 IContentDialogService + SimpleContentDialogCreateOptions。
    /// 包裝 WPF UI 的 ContentDialog，供 ViewModel 以簡單字串呼叫確認對話框。
    /// </summary>
    public class DialogService(IContentDialogService contentDialogService)
    {
        /// <summary>顯示確認對話框，回傳使用者是否點擊主按鈕（例如「確定」）。</summary>
        public async Task<bool> ShowConfirmAsync(string title, string message, string primaryButtonText = "確定", string closeButtonText = "取消")
        {
            var result = await contentDialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
            {
                Title = title,
                Content = message,
                PrimaryButtonText = primaryButtonText,
                CloseButtonText = closeButtonText,
            });

            return result == ContentDialogResult.Primary;
        }
    }
}
