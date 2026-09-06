using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCBuilder.Services;
using System.Reflection;

namespace PCBuilder.ViewModels
{
    /// <summary>
    /// 設定頁 ViewModel：版本資訊、檢查更新、意見回饋、清除所有資料。
    /// 對應原 Setting.razor。
    /// </summary>
    public partial class SettingViewModel : ObservableObject
    {
        private readonly UpdateCheckService _updateService;
        private readonly NotificationService _notificationService;
        private readonly MenuService _menuService;
        private readonly DialogService _dialogService;
        private readonly LinkOpenerService _linkOpenerService;
        private readonly AppPreferences _preferences;

        public SettingViewModel(
            UpdateCheckService updateService,
            NotificationService notificationService,
            MenuService menuService,
            DialogService dialogService,
            LinkOpenerService linkOpenerService,
            AppPreferences preferences)
        {
            _updateService = updateService;
            _notificationService = notificationService;
            _menuService = menuService;
            _dialogService = dialogService;
            _linkOpenerService = linkOpenerService;
            _preferences = preferences;

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            AppVersion = version is null ? "N/A" : $"{version.Major}.{version.Minor}.{version.Build}";

            _ = InitializeAsync();
        }

        [ObservableProperty]
        private string appVersion = "N/A";

        [ObservableProperty]
        private string latestVersion = "N/A";

        [ObservableProperty]
        private bool isCheckingForUpdate;

        private async Task InitializeAsync()
        {
            LatestVersion = await _updateService.GetLatestVersionAsync();
        }

        [RelayCommand]
        private async Task CheckForUpdate()
        {
            IsCheckingForUpdate = true;
            LatestVersion = await _updateService.GetLatestVersionAsync();
            IsCheckingForUpdate = false;

            if (LatestVersion == "檢查失敗")
            {
                _notificationService.ShowError("檢查更新失敗，請確認網路連線");
                return;
            }

            var current = new Version(AppVersion);
            var latest = new Version(LatestVersion);

            if (latest > current)
                await _updateService.NotifyUpdateAvailableAsync(LatestVersion);
            else
                _notificationService.ShowInfo("已經是最新版本!");
        }

        [RelayCommand]
        private void OpenFeedbackEmail() =>
            _linkOpenerService.OpenExternalLink("mailto:a0985209465@gmail.com?subject=【組電腦小幫手 意見回饋】&body=請在此輸入您的意見：\n\n");

        [RelayCommand]
        private async Task ClearAllData()
        {
            var confirmed = await _dialogService.ShowConfirmAsync(
                "確認清除所有資料",
                "此操作將刪除全部菜單及偏好設定，且無法復原。確定要繼續嗎？",
                "確定清除",
                "取消");

            if (!confirmed) return;

            await _menuService.ClearAllDataAsync();
            _preferences.Clear();
        }
    }
}
