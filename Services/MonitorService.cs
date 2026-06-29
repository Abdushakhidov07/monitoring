using System;
using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;

namespace FreeMon.Services
{
    public readonly record struct SensorReading(
        string Id,
        string HardwareName,
        string HardwareType,
        string Name,
        SensorType Type,
        double? Value);

    /// <summary>
    /// Обёртка над LibreHardwareMonitor. Открывает доступ ко всем подсистемам
    /// (CPU, GPU, ОЗУ, материнка, накопители, сеть и т.д.), обновляет датчики
    /// и отдаёт их плоским списком.
    /// </summary>
    public sealed class MonitorService : IDisposable
    {
        private readonly Computer _computer;
        private readonly UpdateVisitor _visitor = new();

        public MonitorService()
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsStorageEnabled = true,
                IsNetworkEnabled = true,
                IsControllerEnabled = true,
                IsBatteryEnabled = true,
                IsPsuEnabled = true,
            };
            _computer.Open();
        }

        /// <summary>Обновить показания всех датчиков.</summary>
        public void Update() => _computer.Accept(_visitor);

        /// <summary>Перечислить все доступные датчики с текущими значениями.</summary>
        public IEnumerable<SensorReading> ReadAll()
        {
            foreach (IHardware hw in _computer.Hardware)
                foreach (SensorReading r in Read(hw))
                    yield return r;
        }

        private static IEnumerable<SensorReading> Read(IHardware hw)
        {
            foreach (ISensor s in hw.Sensors)
            {
                yield return new SensorReading(
                    s.Identifier.ToString(),
                    hw.Name,
                    hw.HardwareType.ToString(),
                    s.Name,
                    s.SensorType,
                    s.Value);
            }

            foreach (IHardware sub in hw.SubHardware)
                foreach (SensorReading r in Read(sub))
                    yield return r;
        }

        public void Dispose()
        {
            try { _computer.Close(); } catch { /* ignore */ }
        }
    }
}
