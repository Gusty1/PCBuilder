using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PCBuilder.Data;
using PCBuilder.Services;
using PCBuilder.ViewModels;
using PCBuilder.Views;
using PCBuilder.Views.Dialogs;
using System.IO;
using System.Net.Http;
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

            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((_, services) =>
                {
                    // WPF UI
                    services.AddSingleton<ISnackbarService, SnackbarService>();
                    services.AddSingleton<IContentDialogService, ContentDialogService>();
                    services.AddSingleton<Services.ThemeService>();

                    // App Services（DataService 由下方 AddHttpClient<DataService> 註冊，勿重複 AddSingleton）
                    services.AddSingleton<AppPreferences>();
                    services.AddSingleton<HardwareService>();
                    services.AddSingleton<MenuService>();
                    services.AddSingleton<CategoryService>();
                    services.AddSingleton<NotificationService>();
                    services.AddSingleton<DialogService>();
                    services.AddSingleton<LinkOpenerService>();

                    // EF Core
                    services.AddDbContext<AppDbContext>(options =>
                        options.UseSqlite($"Data Source={GetDbPath()}"));

                    // HTTP（User-Agent 是必要設定：GitHub Pages 對缺少 UA 的請求會掛起而非直接回應）
                    // DataService 需要維持 Singleton（MainWindowViewModel/HomeViewModel 靠同一實例的 OnStateChanged 事件同步），
                    // 用具名 HttpClient + 工廠委派手動建構，而非 AddHttpClient<DataService>（那預設是 Transient）。
                    services.AddHttpClient("DataService", client =>
                    {
                        client.DefaultRequestHeaders.Add("User-Agent", "PCCustomizer");
                    });
                    services.AddSingleton(sp =>
                    {
                        var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("DataService");
                        return new DataService(httpClient, sp);
                    });
                    services.AddHttpClient<CoolPcService>(client =>
                    {
                        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/138.0.0.0 Safari/537.36");
                        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
                        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-TW,zh;q=0.9,en-US;q=0.8,en;q=0.7");
                    })
                    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false });
                    services.AddHttpClient<UpdateCheckService>(client =>
                    {
                        client.DefaultRequestHeaders.Add("User-Agent", "PCCustomizer-Update-Check");
                    });

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

        private static string GetDbPath()
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PCCustomizer");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "PCCustomizer.db3");
        }
    }
}
