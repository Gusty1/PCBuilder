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
        private readonly ThemeService _themeService;
        private readonly AppSettingsService _appSettingsService;
        private readonly GeminiAiService _geminiAiService;

        public SettingViewModel(
            UpdateCheckService updateService,
            NotificationService notificationService,
            MenuService menuService,
            DialogService dialogService,
            LinkOpenerService linkOpenerService,
            AppPreferences preferences,
            ThemeService themeService,
            AppSettingsService appSettingsService,
            GeminiAiService geminiAiService)
        {
            _appSettingsService = appSettingsService;
            _geminiAiService = geminiAiService;
            _updateService = updateService;
            _notificationService = notificationService;
            _menuService = menuService;
            _dialogService = dialogService;
            _linkOpenerService = linkOpenerService;
            _preferences = preferences;
            _themeService = themeService;

            _themeService.OnChange += HandleThemeChanged;
            IsDarkMode = _themeService.IsDarkMode;

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            AppVersion = version is null ? "N/A" : $"{version.Major}.{version.Minor}.{version.Build}";

            _ = InitializeAsync();
        }

        [ObservableProperty]
        private bool isDarkMode;

        private void HandleThemeChanged() => IsDarkMode = _themeService.IsDarkMode;

        [RelayCommand]
        private void ToggleDarkMode() => _themeService.ToggleDarkMode(!IsDarkMode);

        [ObservableProperty]
        private string appVersion = "N/A";

        [ObservableProperty]
        private string latestVersion = "N/A";

        [ObservableProperty]
        private bool isCheckingForUpdate;

        public string AiProviderDescription => $"{GeminiAiService.Provider}（模型 {GeminiAiService.Model}）";

        [ObservableProperty]
        private string geminiApiKey = "";

        [ObservableProperty]
        private string aiStatus = "";

        [ObservableProperty]
        private bool? isAiStatusSuccess;

        [ObservableProperty]
        private bool isTestingAi;

        private async Task InitializeAsync()
        {
            GeminiApiKey = await _appSettingsService.GetSecretAsync(AppSettingsService.GeminiApiKey) ?? "";
            AiStatus = string.IsNullOrEmpty(GeminiApiKey) ? "尚未設定 API Key" : "已設定（可按「測試連線」確認）";

            LatestVersion = await _updateService.GetLatestVersionAsync();
        }

        [RelayCommand]
        private async Task SaveApiKey()
        {
            await _appSettingsService.SetSecretAsync(AppSettingsService.GeminiApiKey, GeminiApiKey);
            IsAiStatusSuccess = null;
            if (string.IsNullOrWhiteSpace(GeminiApiKey))
            {
                AiStatus = "尚未設定 API Key";
                _notificationService.ShowInfo("已清除 API Key");
            }
            else
            {
                AiStatus = "已設定（可按「測試連線」確認）";
                _notificationService.ShowSuccess("API Key 已加密儲存");
            }
        }

        [RelayCommand]
        private async Task TestApiKey()
        {
            IsTestingAi = true;
            AiStatus = "測試中...";
            IsAiStatusSuccess = null;
            try
            {
                var (success, message) = await _geminiAiService.TestApiKeyAsync(GeminiApiKey);
                AiStatus = message;
                IsAiStatusSuccess = success;
            }
            finally
            {
                IsTestingAi = false;
            }
        }

        [RelayCommand]
        private void OpenApiKeyPage() => _linkOpenerService.OpenExternalLink("https://aistudio.google.com/apikey");

        [RelayCommand]
        private async Task CheckForUpdate()
        {
            IsCheckingForUpdate = true;
            LatestVersion = await _updateService.GetLatestVersionAsync();
            IsCheckingForUpdate = false;

            if (LatestVersion == UpdateCheckService.CheckFailed)
            {
                _notificationService.ShowError("檢查更新失敗，請確認網路連線");
                return;
            }
            if (LatestVersion == UpdateCheckService.NoRelease)
            {
                _notificationService.ShowInfo("目前還沒有發布任何版本");
                return;
            }

            // GitHub tag 可能是 1.2.0-beta 之類無法解析的格式，直接 new Version 會拋例外讓 App 崩潰
            if (!Version.TryParse(AppVersion, out var current) || !Version.TryParse(LatestVersion, out var latest))
            {
                _notificationService.ShowWarning($"無法比較版本（目前 {AppVersion}，最新 {LatestVersion}）");
                return;
            }

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
                "此操作將刪除全部菜單、零件暫存區及偏好設定，且無法復原。確定要繼續嗎？",
                "確定清除",
                "取消");

            if (!confirmed) return;

            await _menuService.ClearAllDataAsync();
            _preferences.Clear();
        }

        public void Dispose()
        {
            _themeService.OnChange -= HandleThemeChanged;
        }
    }
}
