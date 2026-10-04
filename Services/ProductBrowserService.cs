using Microsoft.Web.WebView2.Core;
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
        // 是否能用內嵌瀏覽器：null 代表還沒檢查。不能用時之後都直接開外部瀏覽器，不必每次先跳出一個空視窗再關掉
        private bool? _isWebViewAvailable;

        public async Task OpenAsync(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            _isWebViewAvailable ??= IsWebViewRuntimeInstalled();
            if (_isWebViewAvailable == false)
            {
                linkOpenerService.OpenExternalLink(url);
                return;
            }

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
                // 執行環境有安裝但初始化失敗（例如檔案損毀）：同樣記下來，之後直接用外部瀏覽器
                _isWebViewAvailable = false;
                window.Close();
                linkOpenerService.OpenExternalLink(url);
            }
        }

        private static bool IsWebViewRuntimeInstalled()
        {
            try
            {
                return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
            }
            catch (Exception ex)
            {
                AppLog.Error("找不到 WebView2 執行環境，改用外部瀏覽器開啟連結", ex);
                return false;
            }
        }
    }
}
