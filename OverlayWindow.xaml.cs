using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using FreeMon.Interop;
using FreeMon.Models;

namespace FreeMon
{
    public partial class OverlayWindow : Window
    {
        private readonly ICollectionView _view;
        private bool _locked;

        public OverlayWindow(ObservableCollection<SensorItem> source)
        {
            InitializeComponent();

            // Показываем только отмеченные галочкой датчики
            _view = new CollectionViewSource { Source = source }.View;
            _view.Filter = o => o is SensorItem s && s.IsSelected;
            List.ItemsSource = _view;

            // Перетаскивание мышью (когда оверлей не зафиксирован)
            MouseLeftButtonDown += (_, e) =>
            {
                if (!_locked && e.ButtonState == MouseButtonState.Pressed)
                    DragMove();
            };

            // Применить «клик сквозь» сразу после создания окна, если нужно
            SourceInitialized += (_, _) =>
            {
                if (_locked)
                    NativeMethods.SetClickThrough(this, true);
            };
        }

        /// <summary>Перестроить список (после смены набора выбранных датчиков).</summary>
        public void RefreshList() => _view.Refresh();

        /// <summary>Применить размер шрифта, цвет текста и прозрачность фона.</summary>
        public void ApplyStyle(double fontSize, Color color, double backgroundOpacity)
        {
            List.FontSize = fontSize;
            List.Foreground = new SolidColorBrush(color);

            byte alpha = (byte)(backgroundOpacity * 255);
            RootBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
        }

        public void SetLocked(bool locked)
        {
            _locked = locked;
            if (PresentationSource.FromVisual(this) != null)
                NativeMethods.SetClickThrough(this, locked);
        }

        public void RestorePosition(double left, double top)
        {
            if (!double.IsNaN(left) && left >= 0)
                Left = left;
            if (!double.IsNaN(top) && top >= 0)
                Top = top;
        }
    }
}
