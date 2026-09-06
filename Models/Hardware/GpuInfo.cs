using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PCBuilder.Models.Hardware
{
    /// <summary>
    /// GPU相關模型
    /// </summary>
    public class GpuInfo
    {
        public required string Name { get; set; }
        public double AdapterRamGb { get; set; }
        public required string DriverVersion { get; set; }
        // LHM sensor fields (null = admin not granted or sensor unavailable)
        public float? TemperatureCelsius { get; set; }
        public float? LoadPercent { get; set; }
    }
}
