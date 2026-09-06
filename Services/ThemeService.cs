using CommunityToolkit.Mvvm.ComponentModel;
using Wpf.Ui.Appearance;

namespace PCBuilder.Services
{
    /// <summary>
    /// 主題管理服務的具體實作，改用 WPF UI 的 ApplicationThemeManager 套用主題。
    /// </summary>
    public class ThemeService(AppPreferences preferences) : ObservableObject
    {
        private const string ThemeKey = "AppTheme_IsDark";
        private bool _isDarkMode = false;

        /// <summary>
        /// 當主題狀態改變時觸發的事件。
        /// </summary>
        public event Action? OnChange;

        /// <summary>
        /// 當前是否為暗色模式。
        /// </summary>
        public bool IsDarkMode
        {
            get => _isDarkMode;
            private set
            {
                if (SetProperty(ref _isDarkMode, value))
                    OnChange?.Invoke();
            }
        }

        /// <summary>
        /// (異步) 從使用者偏好設定中載入主題。
        /// </summary>
        public Task LoadThemeAsync()
        {
            IsDarkMode = preferences.GetBool(ThemeKey, false);
            ApplyTheme(IsDarkMode);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 切換亮暗模式。
        /// </summary>
        public void ToggleDarkMode(bool isDarkMode)
        {
            IsDarkMode = isDarkMode;
            preferences.SetBool(ThemeKey, IsDarkMode);
            ApplyTheme(IsDarkMode);
        }

        private static void ApplyTheme(bool isDarkMode) =>
            ApplicationThemeManager.Apply(isDarkMode ? ApplicationTheme.Dark : ApplicationTheme.Light);
    }
}
