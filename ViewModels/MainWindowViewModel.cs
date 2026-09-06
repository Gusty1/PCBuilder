using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PCBuilder.Services;
using PCBuilder.Views.Dialogs;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace PCBuilder.ViewModels
{
    /// <summary>
    /// 主視窗 ViewModel：導覽狀態、全域載入遮罩、主題切換。
    /// </summary>
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly DataService _dataService;
        private readonly HardwareService _hardwareService;
        private readonly Services.ThemeService _themeService;
        private readonly LinkOpenerService _linkOpenerService;
        private readonly IContentDialogService _contentDialogService;
        private readonly IServiceProvider _serviceProvider;

        public MainWindowViewModel(
            DataService dataService,
            HardwareService hardwareService,
            Services.ThemeService themeService,
            LinkOpenerService linkOpenerService,
            IContentDialogService contentDialogService,
            IServiceProvider serviceProvider)
        {
            _dataService = dataService;
            _hardwareService = hardwareService;
            _themeService = themeService;
            _linkOpenerService = linkOpenerService;
            _contentDialogService = contentDialogService;
            _serviceProvider = serviceProvider;

            _dataService.OnStateChanged += HandleDataStateChanged;
            _themeService.OnChange += HandleThemeChanged;

            IsGlobalLoading = _dataService.IsGlobalLoading;
            LoadingMessage = _dataService.LoadingMessage;
            IsDarkMode = _themeService.IsDarkMode;

            _ = InitializeAsync();
        }

        [ObservableProperty]
        private bool isGlobalLoading = true;

        [ObservableProperty]
        private string loadingMessage = "更新原價屋資訊中...";

        [ObservableProperty]
        private bool isDarkMode;

        private async Task InitializeAsync()
        {
            await _themeService.LoadThemeAsync();
            IsDarkMode = _themeService.IsDarkMode;

            // fire-and-forget：啟動時背景初始化資料與硬體掃描，完成後透過事件通知
            _ = _dataService.SeedDataIfNeededAsync();
            _ = _hardwareService.ScanComputerInfoAsync();
        }

        private void HandleDataStateChanged()
        {
            IsGlobalLoading = _dataService.IsGlobalLoading;
            LoadingMessage = _dataService.LoadingMessage;
        }

        private void HandleThemeChanged() => IsDarkMode = _themeService.IsDarkMode;

        [RelayCommand]
        private void ToggleDarkMode() => _themeService.ToggleDarkMode(!IsDarkMode);

        [RelayCommand]
        private void OpenExternalLink(string url) => _linkOpenerService.OpenExternalLink(url);

        [RelayCommand]
        private async Task OpenComputerInfo()
        {
            var dialogContent = _serviceProvider.GetRequiredService<ComputerInfoDialog>();
            var dialog = new ContentDialog
            {
                Title = "我的電腦資訊",
                Content = dialogContent,
                CloseButtonText = "關閉",
            };

            await _contentDialogService.ShowAsync(dialog, CancellationToken.None);
        }
    }
}
