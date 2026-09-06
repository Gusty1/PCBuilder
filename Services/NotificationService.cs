using Wpf.Ui;
using Wpf.Ui.Controls;

namespace PCBuilder.Services
{
    /// <summary>
    /// 通知服務的實作，改用 WPF UI 的 ISnackbarService。
    /// </summary>
    public class NotificationService(ISnackbarService snackbarService)
    {
        public void ShowSuccess(string message) =>
            snackbarService.Show("成功", message, ControlAppearance.Success, null, TimeSpan.FromSeconds(3));

        public void ShowError(string message) =>
            snackbarService.Show("錯誤", message, ControlAppearance.Danger, null, TimeSpan.FromSeconds(4));

        public void ShowInfo(string message) =>
            snackbarService.Show("提示", message, ControlAppearance.Info, null, TimeSpan.FromSeconds(3));

        public void ShowWarning(string message) =>
            snackbarService.Show("警告", message, ControlAppearance.Caution, null, TimeSpan.FromSeconds(3));
    }
}
