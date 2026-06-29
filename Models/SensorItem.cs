using System;
using System.ComponentModel;
using System.Globalization;
using LibreHardwareMonitor.Hardware;

namespace FreeMon.Models
{
    public enum ValueKind
    {
        Sensor,
        Fps,
        FrameTime
    }

    /// <summary>
    /// Одна строка мониторинга. Используется и в таблице, и в оверлее.
    /// Реализует INotifyPropertyChanged, поэтому значения в интерфейсе
    /// обновляются автоматически при изменении Value.
    /// </summary>
    public sealed class SensorItem : INotifyPropertyChanged
    {
        public string Id { get; }
        public string HardwareName { get; }
        public string HardwareType { get; }
        public string Name { get; }
        public ValueKind Kind { get; }
        public SensorType SensorType { get; }

        public SensorItem(string id, string hardwareName, string hardwareType,
                          string name, SensorType sensorType, ValueKind kind = ValueKind.Sensor)
        {
            Id = id;
            HardwareName = hardwareName;
            HardwareType = hardwareType;
            Name = name;
            SensorType = sensorType;
            Kind = kind;
        }

        private double? _value;
        public double? Value
        {
            get => _value;
            set
            {
                if (Nullable.Equals(_value, value)) return;
                _value = value;
                OnChanged(nameof(Value));
                OnChanged(nameof(Pretty));
                OnChanged(nameof(OverlayLine));
            }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnChanged(nameof(IsSelected));
            }
        }

        public string TypeName => Kind switch
        {
            ValueKind.Fps => "FPS",
            ValueKind.FrameTime => "Frametime",
            _ => SensorType.ToString()
        };

        public string Pretty => Kind switch
        {
            ValueKind.Fps => _value.HasValue
                ? _value.Value.ToString("0", CultureInfo.InvariantCulture) + " FPS"
                : "—",
            ValueKind.FrameTime => _value.HasValue
                ? _value.Value.ToString("0.0", CultureInfo.InvariantCulture) + " ms"
                : "—",
            _ => Formatting.Pretty(_value, SensorType)
        };

        // То, как строка выглядит в оверлее: короткая подпись + значение
        public string OverlayLine => Name + ": " + Pretty;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
