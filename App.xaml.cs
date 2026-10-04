using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PCBuilder.Data;
using PCBuilder.Services;
using PCBuilder.ViewModels;
using PCBuilder.Views;
using PCBuilder.Views.Dialogs;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Windows;
using Wpf.Ui;

namespace PCBuilder
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private IHost? _host;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // SQLite 不會自動建立資料夾，要在建立 DbContext 之前先建好
            Directory.CreateDirectory(AppPaths.DataFolder);
            _ = UpdateCheckService.CleanUpAfterUpdateAsync();

            _host = Host.CreateDefaultBuilder()
                // EF Core 預設 Information 等級會把首次匯入的 7000 多筆 INSERT 全部輸出；
                // HttpClient 每個請求都會輸出（例如檢查更新的 404 其實是「尚未發布」），看起來像錯誤。兩者都只保留警告以上
                .ConfigureLogging(logging => logging
                    .AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning)
                    .AddFilter("System.Net.Http.HttpClient", LogLevel.Warning))
                .ConfigureServices((_, services) =>
                {
                    // WPF UI
                    services.AddSingleton<ISnackbarService, SnackbarService>();
                    services.AddSingleton<IContentDialogService, ContentDialogService>();
                    services.AddSingleton<Wpf.Ui.Abstractions.INavigationViewPageProvider, NavigationPageProvider>();
                    services.AddSingleton<INavigationService, NavigationService>();
                    services.AddSingleton<Services.ThemeService>();

                    // App Services（DataService 由下方 AddHttpClient<DataService> 註冊，勿重複 AddSingleton）
                    services.AddSingleton<AppPreferences>();
                    services.AddSingleton<HardwareService>();
                    services.AddSingleton<MenuService>();
                    services.AddSingleton<CategoryService>();
                    services.AddSingleton<NotificationService>();
                    services.AddSingleton<DialogService>();
                    services.AddSingleton<LinkOpenerService>();
                    services.AddSingleton<ProductBrowserService>();

                    // EF Core：用工廠讓每次操作建立短暫的 DbContext。服務都是 Singleton，若直接注入 DbContext，
                    // 同一個 DbContext 會活到 App 關閉、一直追蹤第一次讀到的資料，背景更新商品後仍讀到舊價格
                    services.AddDbContextFactory<AppDbContext>(options =>
                        options.UseSqlite($"Data Source={AppPaths.DbPath}"));

                    // 所有 HttpClient 共用：連線時先試 IPv4（原因見 ConnectIpv4FirstAsync）；
                    // 要求 gzip 壓縮：商品資料 5.8 MB 壓縮後只有約 450 KB，實測下載從 14～19 秒降到 2～2.5 秒
                    services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(
                        () => new SocketsHttpHandler
                        {
                            ConnectCallback = ConnectIpv4FirstAsync,
                            AutomaticDecompression = DecompressionMethods.All,
                        }));

                    // HTTP（User-Agent 是必要設定：GitHub Pages 對缺少 UA 的請求會掛起而非直接回應）
                    // DataService 需要維持 Singleton（MainWindowViewModel/HomeViewModel 靠同一實例的 OnStateChanged 事件同步），
                    // 用具名 HttpClient + 工廠委派手動建構，而非 AddHttpClient<DataService>（那預設是 Transient）。
                    services.AddHttpClient("DataService", client =>
                    {
                        client.DefaultRequestHeaders.Add("User-Agent", "PCBuilder");
                    });
                    services.AddSingleton(sp =>
                    {
                        var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("DataService");
                        return new DataService(httpClient, sp, sp.GetRequiredService<AppPreferences>());
                    });
                    services.AddHttpClient<CoolPcService>(client =>
                    {
                        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/138.0.0.0 Safari/537.36");
                        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
                        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-TW,zh;q=0.9,en-US;q=0.8,en;q=0.7");
                    })
                    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false, ConnectCallback = ConnectIpv4FirstAsync });
                    services.AddHttpClient<UpdateCheckService>(client =>
                    {
                        client.DefaultRequestHeaders.Add("User-Agent", "PCBuilder-Update-Check");
                    });

                    // AI（Singleton 服務透過 IHttpClientFactory 每次建立 HttpClient，避免長期持有同一個連線）
                    services.AddHttpClient(GeminiAiService.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(120));
                    services.AddSingleton<AppSettingsService>();
                    services.AddSingleton<GeminiAiService>();
                    services.AddSingleton<AiAssistantService>();
                    services.AddSingleton<AiChatViewModel>();

                    // Views / ViewModels
                    services.AddSingleton<MainWindow>();
                    services.AddSingleton<MainWindowViewModel>();
                    services.AddTransient<HomeView>();
                    services.AddTransient<HomeViewModel>();
                    services.AddTransient<MenuView>();
                    services.AddTransient<MenuViewModel>();
                    services.AddTransient<SettingView>();
                    services.AddTransient<SettingViewModel>();
                    services.AddTransient<ComputerInfoDialog>();
                    services.AddTransient<ComputerInfoDialogViewModel>();
                })
                .Build();

            _host.Start();

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _host?.StopAsync().GetAwaiter().GetResult();
            _host?.Dispose();
            base.OnExit(e);
        }

        /// <summary>
        /// 有些網路的 IPv6 不通（例如開過 VPN 後），.NET 會依序嘗試每個位址、IPv6 每個都要等到連線逾時，
        /// 商品資料所在的 gusty1.github.io 有多個 IPv6 位址，累計超過下載逾時就「資料更新失敗」。
        /// 改成先試 IPv4、沒有 IPv4 位址時才用 IPv6（純 IPv6 網路仍可連線）。
        /// </summary>
        private static async ValueTask<Stream> ConnectIpv4FirstAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
        {
            var addresses = (await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken))
                .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                .ToArray();

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }
}
