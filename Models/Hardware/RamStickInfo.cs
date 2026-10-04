using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PCBuilder.Models.Hardware
{
    /// <summary>
    /// RAM相關模型
    /// </summary>
    public class RamStickInfo
    {
        public double CapacityGb { get; set; }
        public uint SpeedMhz { get; set; }
    }
}
