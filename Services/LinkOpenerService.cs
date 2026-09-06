using System.Diagnostics;

namespace PCBuilder.Services
{
    /// <summary>
    /// 以 Process.Start 開啟外部連結，取代 MAUI 的 Launcher。
    /// </summary>
    public class LinkOpenerService(NotificationService notificationService)
    {
        public void OpenExternalLink(string url)
        {
            if (string.IsNullOrEmpty(url)) return;

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"開啟連結時發生錯誤: {ex.Message}");
                notificationService.ShowError("開啟連結時發生錯誤");
            }
        }
    }
}
