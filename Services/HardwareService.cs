using CommunityToolkit.Mvvm.ComponentModel;
using LibreHardwareMonitor.Hardware;
using PCBuilder.Models.Hardware;
using System.Diagnostics;
using System.Management;
using System.Security.Principal;

namespace PCBuilder.Services
{
    /// <summary>
    /// 取得電腦硬體資訊的服務實作。
    /// 靜態欄位（名稱、容量、規格）來自 WMI；感測器數值（溫度、負載、風扇）來自 LibreHardwareMonitor。
    /// LHM 感測器需要以系統管理員身份執行，且 LHM 0.9.5 起需要系統已安裝 PawnIO 驅動；條件不足時感測器欄位保持 null。
    /// </summary>
    public class HardwareService : ObservableObject, IDisposable
    {
        private ComputerInfo? _cachedInfo;
        public ComputerInfo CurrentComputerInfo => _cachedInfo!;

        private bool _isScanning = false;
        /// <summary>
        /// 指示當前是否正在掃描硬體。
        /// </summary>
        public bool IsScanning
        {
            get => _isScanning;
            private set
            {
                if (SetProperty(ref _isScanning, value))
                    OnStateChanged?.Invoke();
            }
        }

        /// <summary>
        /// 當掃描狀態或資料變更時觸發的事件。
        /// </summary>
        public event Action? OnStateChanged;

        // ponytail: Computer kept open for app lifetime so live refresh reuses it without reinit.
        private Computer? _lhm;
        private readonly UpdateVisitor _visitor = new();

        private static bool IsAdmin()
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        private const double BytesPerGb = 1024.0 * 1024.0 * 1024.0;

        public async Task ScanComputerInfoAsync()
        {
            if (_cachedInfo != null || IsScanning) return;

            try
            {
                IsScanning = true;
                _cachedInfo = await Task.Run(() =>
                {
                    var ci = new ComputerInfo();
                    // 每種硬體分開讀：某一項 WMI 查詢失敗（例如驅動回傳意外的型別）時，其他硬體資訊照樣顯示
                    TryRead("主機板", () => ReadMotherboard(ci));
                    TryRead("CPU", () => ReadCpu(ci));
                    TryRead("顯示卡", () => ReadGpus(ci));
                    TryRead("記憶體", () => ReadRam(ci));
                    TryRead("硬碟", () => ReadDisks(ci));
                    // LHM 感測器需要系統管理員權限，沒有權限時跳過
                    if (IsAdmin()) TryRead("感測器", () => OpenSensors(ci));
                    return ci;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"獲取硬體資訊時發生錯誤: {ex.Message}");
            }
            finally
            {
                IsScanning = false;
            }
        }

        private static void TryRead(string part, Action read)
        {
            try
            {
                read();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"讀取{part}資訊失敗: {ex.Message}");
            }
        }

        // WMI 回傳的數值裝箱型別依驅動而異（常見是 uint，也可能是 ulong），直接轉型在型別不符時會 InvalidCastException，一律用 Convert
        private static uint? ToUInt32(object? value) => value is null ? null : Convert.ToUInt32(value);

        private static ulong ToUInt64(object? value) => value is null ? 0UL : Convert.ToUInt64(value);

        private static double ToGb(ulong bytes) => Math.Round(bytes / BytesPerGb, 2);

        private static IEnumerable<ManagementObject> Query(string wql)
        {
            using var searcher = new ManagementObjectSearcher(wql);
            return [.. searcher.Get().OfType<ManagementObject>()];
        }

        private static void ReadMotherboard(ComputerInfo ci)
        {
            if (Query("SELECT * FROM Win32_BaseBoard").FirstOrDefault() is not { } board) return;
            ci.MotherboardManufacturer = board["Manufacturer"]?.ToString() ?? "N/A";
            ci.MotherboardProduct = board["Product"]?.ToString() ?? "N/A";
        }

        private static void ReadCpu(ComputerInfo ci)
        {
            if (Query("SELECT * FROM Win32_Processor").FirstOrDefault() is not { } cpu) return;
            ci.CpuName = cpu["Name"]?.ToString()?.Trim() ?? "N/A";
            ci.CpuCores = ToUInt32(cpu["NumberOfCores"]);
            ci.CpuThreads = ToUInt32(cpu["NumberOfLogicalProcessors"]);
            ci.CpuMaxClockSpeedMhz = ToUInt32(cpu["MaxClockSpeed"]);
        }

        // GPU：名稱 + 驅動版本來自 WMI；VRAM 用 ulong 避免 >4GB overflow
        private static void ReadGpus(ComputerInfo ci)
        {
            foreach (var gpu in Query("SELECT * FROM Win32_VideoController"))
            {
                ci.Gpus!.Add(new GpuInfo
                {
                    Name = gpu["Caption"]?.ToString() ?? "N/A",
                    AdapterRamGb = ToGb(ToUInt64(gpu["AdapterRAM"])),
                    DriverVersion = gpu["DriverVersion"]?.ToString() ?? "N/A"
                });
            }
        }

        private static void ReadRam(ComputerInfo ci)
        {
            foreach (var stick in Query("SELECT * FROM Win32_PhysicalMemory"))
            {
                ci.RamSticks!.Add(new RamStickInfo
                {
                    CapacityGb = ToGb(ToUInt64(stick["Capacity"])),
                    SpeedMhz = ToUInt32(stick["Speed"]) ?? 0
                });
            }
            ci.TotalPhysicalMemoryGb = Math.Round(ci.RamSticks!.Sum(r => r.CapacityGb), 2);
        }

        private static void ReadDisks(ComputerInfo ci)
        {
            foreach (var disk in Query("SELECT * FROM Win32_DiskDrive"))
            {
                var sizeBytes = ToUInt64(disk["Size"]);
                if (sizeBytes == 0) continue;
                ci.Disks!.Add(new DiskInfo
                {
                    Model = disk["Model"]?.ToString() ?? "N/A",
                    SizeGb = ToGb(sizeBytes)
                });
            }
        }

        private void OpenSensors(ComputerInfo ci)
        {
            _lhm = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsStorageEnabled = true,
                IsMotherboardEnabled = true,
            };
            _lhm.Open();
            ReadLhmSensors(ci);
        }

        private void ReadLhmSensors(ComputerInfo ci)
        {
            if (_lhm is null) return;
            _lhm.Accept(_visitor);

            foreach (var hw in _lhm.Hardware)
            {
                switch (hw.HardwareType)
                {
                    case HardwareType.Cpu:
                        foreach (var s in hw.Sensors)
                        {
                            if (s.SensorType == SensorType.Temperature && s.Name == "CPU Package")
                                ci.CpuTemperatureCelsius = s.Value;
                            else if (s.SensorType == SensorType.Load && s.Name == "CPU Total")
                                ci.CpuLoadPercent = s.Value;
                        }
                        foreach (var core in hw.SubHardware)
                            foreach (var s in core.Sensors)
                                if (s.SensorType == SensorType.Clock && s.Name.StartsWith("CPU Core"))
                                    ci.CpuCoreClocksMhz.Add(s.Value ?? 0f);
                        break;

                    case HardwareType.GpuNvidia:
                    case HardwareType.GpuAmd:
                    case HardwareType.GpuIntel:
                        var gpu = ci.Gpus!.FirstOrDefault(g => g.Name == hw.Name);
                        if (gpu is null) break;
                        foreach (var s in hw.Sensors)
                        {
                            if (s.SensorType == SensorType.Temperature && s.Name == "GPU Core")
                                gpu.TemperatureCelsius = s.Value;
                            else if (s.SensorType == SensorType.Load && s.Name == "GPU Core")
                                gpu.LoadPercent = s.Value;
                        }
                        break;

                    case HardwareType.Memory:
                        foreach (var s in hw.Sensors)
                        {
                            if (s.SensorType == SensorType.Data && s.Name == "Memory Used")
                                ci.RamUsedGb = s.Value;
                            else if (s.SensorType == SensorType.Data && s.Name == "Memory Available")
                                ci.RamAvailableGb = s.Value;
                        }
                        break;

                    case HardwareType.Storage:
                        var disk = ci.Disks!.FirstOrDefault(d => d.Model == hw.Name);
                        if (disk is null) break;
                        foreach (var s in hw.Sensors)
                        {
                            if (s.SensorType == SensorType.Temperature)
                                disk.TemperatureCelsius = s.Value;
                            else if (s.SensorType == SensorType.Level && s.Name == "Remaining Life")
                                disk.SmartLifeRemainingPercent = s.Value;
                        }
                        break;

                    case HardwareType.Motherboard:
                        foreach (var sub in hw.SubHardware)
                        {
                            sub.Update();
                            foreach (var s in sub.Sensors)
                                if (s.SensorType == SensorType.Fan)
                                    ci.FanSpeeds.Add((s.Name, s.Value ?? 0f));
                        }
                        break;
                }
            }
        }

        public void Dispose()
        {
            _lhm?.Close();
            _lhm = null;
        }
    }

    /// <summary>LHM 標準 Visitor，呼叫 Accept 前必須先套用以更新感測器值。</summary>
    internal sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware) sub.Accept(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}
