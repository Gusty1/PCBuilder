using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PCBuilder.Models.Hardware
{
    /// <summary>
    /// 存放所有電腦硬體與系統資訊的主模型。
    /// </summary>
    public class ComputerInfo
    {
        public string? OsArchitecture { get; set; }
        public string? MotherboardManufacturer { get; set; }
        public string? MotherboardProduct { get; set; }
        public string? CpuName { get; set; }
        public uint? CpuCores { get; set; }
        public uint? CpuThreads { get; set; }
        public uint? CpuMaxClockSpeedMhz { get; set; }
        public double? TotalPhysicalMemoryGb { get; set; }
        public List<GpuInfo>? Gpus { get; set; } = [];
        public List<RamStickInfo>? RamSticks { get; set; } = [];
        public List<DiskInfo>? Disks { get; set; } = [];
        // LHM sensor fields (null = admin not granted or sensor unavailable)
        public float? CpuTemperatureCelsius { get; set; }
        public float? CpuLoadPercent { get; set; }
        public List<float> CpuCoreClocksMhz { get; set; } = [];
        public float? RamUsedGb { get; set; }
        public float? RamAvailableGb { get; set; }
        public List<(string Name, float Rpm)> FanSpeeds { get; set; } = [];
    }
}
