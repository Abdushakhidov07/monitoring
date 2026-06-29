using System.Globalization;
using LibreHardwareMonitor.Hardware;

namespace FreeMon.Models
{
    /// <summary>
    /// Превращает числовое значение датчика в красивую строку с единицами измерения.
    /// </summary>
    public static class Formatting
    {
        private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        public static string Pretty(double? value, SensorType type)
        {
            if (!value.HasValue)
                return "—";

            double v = value.Value;

            return type switch
            {
                SensorType.Voltage => v.ToString("0.000", Ci) + " V",
                SensorType.Current => v.ToString("0.000", Ci) + " A",
                SensorType.Power => v.ToString("0.0", Ci) + " W",
                SensorType.Clock => v.ToString("0", Ci) + " MHz",
                SensorType.Temperature => v.ToString("0.0", Ci) + " °C",
                SensorType.Load => v.ToString("0.0", Ci) + " %",
                SensorType.Level => v.ToString("0.0", Ci) + " %",
                SensorType.Control => v.ToString("0.0", Ci) + " %",
                SensorType.Humidity => v.ToString("0.0", Ci) + " %",
                SensorType.Frequency => v.ToString("0", Ci) + " Hz",
                SensorType.Fan => v.ToString("0", Ci) + " RPM",
                SensorType.Flow => v.ToString("0.0", Ci) + " L/h",
                SensorType.Data => v.ToString("0.0", Ci) + " GB",
                SensorType.SmallData => v.ToString("0.0", Ci) + " MB",
                SensorType.Energy => v.ToString("0", Ci) + " mWh",
                SensorType.Noise => v.ToString("0.0", Ci) + " dBA",
                SensorType.TimeSpan => v.ToString("0", Ci) + " s",
                SensorType.Throughput => Throughput(v),
                SensorType.Factor => v.ToString("0.000", Ci),
                _ => v.ToString("0.0", Ci)
            };
        }

        private static string Throughput(double bytesPerSecond)
        {
            if (bytesPerSecond >= 1024d * 1024d)
                return (bytesPerSecond / (1024d * 1024d)).ToString("0.0", Ci) + " MB/s";
            if (bytesPerSecond >= 1024d)
                return (bytesPerSecond / 1024d).ToString("0.0", Ci) + " KB/s";
            return bytesPerSecond.ToString("0", Ci) + " B/s";
        }
    }
}
