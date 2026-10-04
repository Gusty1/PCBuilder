using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using PCBuilder.Services;
using System.IO;
using System.Windows;
using Wpf.Ui.Controls;

namespace PCBuilder.Views
{
    /// <summary>
    /// App 內的瀏覽視窗（WebView2），開啟商品連結與相關連結。由 ProductBrowserService 建立與重複使用。
    /// </summary>
    public partial class ProductBrowserWindow : FluentWindow
    {
        private const string DefaultTitle = "網頁";

        private readonly LinkOpenerService _linkOpenerService;
        private readonly Task _initialization;

        public ProductBrowserWindow(LinkOpenerService linkOpenerService)
        {
            InitializeComponent();
            _linkOpenerService = linkOpenerService;

            // 瀏覽資料（登入狀態、快取）放在 PCBuilder 自己的資料夾；預設位置在執行檔旁邊，裝在 Program Files 時會沒有寫入權限
            WebView.CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = Path.Combine(AppPaths.DataFolder, "WebView2"),
            };
            _initialization = InitializeWebViewAsync();
        }

        /// <summary>開啟網址；WebView2 無法使用（例如電腦沒有執行環境）時回傳 false，由呼叫端改用外部瀏覽器。</summary>
        public async Task<bool> NavigateAsync(string url)
        {
            try
            {
                await _initialization;
                WebView.CoreWebView2.Navigate(url);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error($"內嵌瀏覽器無法開啟 {url}", ex);
                return false;
            }
        }

        // EnsureCoreWebView2Async 要等視窗顯示、控制項有視窗代碼後才會完成
        private async Task InitializeWebViewAsync()
        {
            await WebView.EnsureCoreWebView2Async();
            var core = WebView.CoreWebView2;

            core.DocumentTitleChanged += (_, _) =>
            {
                var title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? DefaultTitle : core.DocumentTitle;
                Title = title;
                WindowTitleBar.Title = title;
            };
            core.SourceChanged += (_, _) => AddressText.Text = core.Source;
            core.HistoryChanged += (_, _) =>
            {
                BackButton.IsEnabled = core.CanGoBack;
                ForwardButton.IsEnabled = core.CanGoForward;
            };
            core.NavigationStarting += (_, _) => LoadingRing.Visibility = Visibility.Visible;
            core.NavigationCompleted += (_, _) => LoadingRing.Visibility = Visibility.Collapsed;
            // 網頁要開新視窗（target=_blank）時改在同一個視窗開啟，避免跳出沒有工具列的 WebView2 預設視窗
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                core.Navigate(e.Uri);
            };
        }

        private void Back_Click(object sender, RoutedEventArgs e) => WebView.CoreWebView2?.GoBack();

        private void Forward_Click(object sender, RoutedEventArgs e) => WebView.CoreWebView2?.GoForward();

        private void Reload_Click(object sender, RoutedEventArgs e) => WebView.CoreWebView2?.Reload();

        private void OpenInBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (WebView.CoreWebView2?.Source is { Length: > 0 } url)
                _linkOpenerService.OpenExternalLink(url);
        }

        protected override void OnClosed(EventArgs e)
        {
            WebView.Dispose();
            base.OnClosed(e);
        }
    }
}
