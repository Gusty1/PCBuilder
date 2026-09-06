using CommunityToolkit.Mvvm.ComponentModel;
using PCBuilder.Models.Hardware;
using PCBuilder.Services;

namespace PCBuilder.ViewModels
{
    /// <summary>
    /// 電腦資訊對話框 ViewModel：代理 IHardwareService 的掃描狀態與結果。
    /// 對應原 ComputerInfoDialog.razor。
    /// </summary>
    public partial class ComputerInfoDialogViewModel : ObservableObject, IDisposable
    {
        private readonly HardwareService _hardwareService;

        public ComputerInfoDialogViewModel(HardwareService hardwareService)
        {
            _hardwareService = hardwareService;
            _hardwareService.OnStateChanged += HandleStateChanged;
        }

        public bool IsScanning => _hardwareService.IsScanning;

        public ComputerInfo? Info => _hardwareService.CurrentComputerInfo;

        private void HandleStateChanged()
        {
            OnPropertyChanged(nameof(IsScanning));
            OnPropertyChanged(nameof(Info));
        }

        public void Dispose()
        {
            _hardwareService.OnStateChanged -= HandleStateChanged;
        }
    }
}
