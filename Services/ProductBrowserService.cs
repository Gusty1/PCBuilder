using PCBuilder.Views;
using System.Windows;

namespace PCBuilder.Services
{
    /// <summary>
    /// 在 App 內的瀏覽視窗開啟商品連結與相關連結（天梯圖、組裝模擬器）。整個 App 只有一個這樣的視窗，點別的連結會換內容並帶到最前面；
    /// WebView2 無法使用時改用外部瀏覽器。
    /// </summary>
    public class ProductBrowserService(LinkOpenerService linkOpenerService)
    {
        private ProductBrowserWindow? _window;

        public async Task OpenAsync(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            if (_window is null)
            {
                _window = new ProductBrowserWindow(linkOpenerService);
                _window.Closed += (_, _) => _window = null;
            }

            var window = _window;
            window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();

            if (!await window.NavigateAsync(url))
            {
                window.Close();
                linkOpenerService.OpenExternalLink(url);
            }
        }
    }
}
