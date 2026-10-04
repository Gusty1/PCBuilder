using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PCBuilder.Models.Hardware
{
    /// <summary>
    /// 硬碟資訊相關模型
    /// </summary>
    public class DiskInfo
    {
        public required string Model { get; set; }
        public double SizeGb { get; set; }
        // LHM sensor fields (null = admin not granted or sensor unavailable)
        public float? TemperatureCelsius { get; set; }
        public float? SmartLifeRemainingPercent { get; set; }
    }
}
