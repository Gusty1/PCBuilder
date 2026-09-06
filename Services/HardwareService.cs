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
    /// LHM 感測器需要以系統管理員身份執行，未取得權限時感測器欄位保持 null。
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

        public async Task ScanComputerInfoAsync()
        {
            if (_cachedInfo != null || IsScanning) return;

            try
            {
                IsScanning = true;

                var info = await Task.Run(() =>
                {
                    var ci = new ComputerInfo();

                    // ── WMI：靜態欄位（不需 admin） ──────────────────────────────

                    using var boardSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_BaseBoard");
                    var board = boardSearcher.Get().OfType<ManagementObject>().FirstOrDefault();
                    if (board != null)
                    {
                        ci.MotherboardManufacturer = board["Manufacturer"]?.ToString() ?? "N/A";
                        ci.MotherboardProduct      = board["Product"]?.ToString() ?? "N/A";
                    }

                    using var cpuSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
                    var cpu = cpuSearcher.Get().OfType<ManagementObject>().FirstOrDefault();
                    if (cpu != null)
                    {
                        ci.CpuName            = cpu["Name"]?.ToString()?.Trim() ?? "N/A";
                        ci.CpuCores           = (uint)cpu["NumberOfCores"];
                        ci.CpuThreads         = (uint)cpu["NumberOfLogicalProcessors"];
                        ci.CpuMaxClockSpeedMhz = (uint)cpu["MaxClockSpeed"];
                    }

                    // GPU：名稱 + 驅動版本來自 WMI；VRAM 用 ulong 避免 >4GB overflow
                    using var gpuSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                    foreach (ManagementObject gpu in gpuSearcher.Get())
                    {
                        var vramBytes = (ulong?)gpu["AdapterRAM"] ?? 0UL;
                        ci.Gpus!.Add(new GpuInfo
                        {
                            Name          = gpu["Caption"]?.ToString() ?? "N/A",
                            AdapterRamGb  = Math.Round(vramBytes / (1024.0 * 1024.0 * 1024.0), 2),
                            DriverVersion = gpu["DriverVersion"]?.ToString() ?? "N/A"
                        });
                    }

                    using var ramSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory");
                    foreach (ManagementObject stick in ramSearcher.Get())
                    {
                        var capBytes = (ulong?)stick["Capacity"] ?? 0UL;
                        ci.RamSticks!.Add(new RamStickInfo
                        {
                            CapacityGb = Math.Round(capBytes / (1024.0 * 1024.0 * 1024.0), 2),
                            SpeedMhz   = (uint?)stick["Speed"] ?? 0
                        });
                    }
                    ci.TotalPhysicalMemoryGb = Math.Round(ci.RamSticks!.Sum(r => r.CapacityGb), 2);

                    using var diskSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
                    foreach (ManagementObject disk in diskSearcher.Get())
                    {
                        var sizeBytes = (ulong?)disk["Size"] ?? 0UL;
                        if (sizeBytes > 0)
                        {
                            ci.Disks!.Add(new DiskInfo
                            {
                                Model  = disk["Model"]?.ToString() ?? "N/A",
                                SizeGb = Math.Round(sizeBytes / (1024.0 * 1024.0 * 1024.0), 2)
                            });
                        }
                    }

                    // ── LHM：感測器欄位（需要 admin，無權限時靜默跳過） ──────────

                    if (IsAdmin())
                    {
                        try
                        {
                            _lhm = new Computer
                            {
                                IsCpuEnabled         = true,
                                IsGpuEnabled         = true,
                                IsMemoryEnabled      = true,
                                IsStorageEnabled     = true,
                                IsMotherboardEnabled = true,
                            };
                            _lhm.Open();
                            ReadLhmSensors(ci);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"LHM 初始化失敗: {ex.Message}");
                        }
                    }

                    return ci;
                });

                _cachedInfo = info;
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
