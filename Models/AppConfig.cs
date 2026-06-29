using System.Collections.Generic;

namespace FreeMon.Models
{
    /// <summary>
    /// Настройки приложения. Сохраняются в %AppData%\FreeMon\config.json
    /// </summary>
    public sealed class AppConfig
    {
        // Идентификаторы датчиков, выбранных для показа в оверлее
        public List<string> SelectedSensorIds { get; set; } = new();

        // Как часто обновлять значения, мс
        public int IntervalMs { get; set; } = 1000;

        // Оформление оверлея
        public double FontSize { get; set; } = 16;
        public string TextColor { get; set; } = "Lime";
        public double BackgroundOpacity { get; set; } = 0.5;

        // Позиция оверлея на экране
        public double OverlayLeft { get; set; } = 40;
        public double OverlayTop { get; set; } = 40;

        // Оверлей зафиксирован (клики проходят сквозь него)
        public bool Locked { get; set; } = false;

        // Включён ли счётчик FPS
        public bool FpsEnabled { get; set; } = false;
    }
}
