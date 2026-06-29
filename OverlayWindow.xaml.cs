using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using FreeMon.Interop;
using FreeMon.Models;

namespace FreeMon
{
    public partial class OverlayWindow : Window
    {
        private readonly ObservableCollection<SensorItem> _source;
        private bool _locked;

        // текущий стиль
        private double _baseFont = 14;
        private Brush _valueBrush = Brushes.White;
        private double _bgOpacity = 0.92;

        private bool _fpsVisible;
        private int _dotTick;

        // Периодически поднимает оверлей поверх всех окон (для игр «без рамки»)
        private readonly DispatcherTimer _topmost =
            new() { Interval = TimeSpan.FromMilliseconds(700) };

        private static readonly Brush MutedBrush =
            new SolidColorBrush(Color.FromRgb(0x86, 0x8C, 0x95));

        public OverlayWindow(ObservableCollection<SensorItem> source)
        {
            InitializeComponent();
            _source = source;

            MouseLeftButtonDown += (_, e) =>
            {
                if (!_locked && e.ButtonState == MouseButtonState.Pressed)
                    DragMove();
            };

            SourceInitialized += (_, _) =>
            {
                if (_locked) NativeMethods.SetClickThrough(this, true);
                NativeMethods.ForceTopMost(this);
                _topmost.Tick += (_, _) => NativeMethods.ForceTopMost(this);
                _topmost.Start();
            };

            Closed += (_, _) => _topmost.Stop();
        }

        // ---------- построение списка ----------

        public void RefreshList() => Build();

        private void Build()
        {
            GroupsHost.Children.Clear();

            List<SensorItem> selected = _source
                .Where(s => s.IsSelected && s.Kind == ValueKind.Sensor)
                .ToList();

            var groups = selected
                .GroupBy(s => s.HardwareName)
                .ToList();

            bool first = true;
            foreach (var group in groups)
            {
                if (!first)
                    GroupsHost.Children.Add(Divider());
                first = false;

                string hwType = group.First().HardwareType;
                GroupsHost.Children.Add(HeaderFor(group.Key, hwType));

                foreach (SensorItem item in group)
                    GroupsHost.Children.Add(RowFor(item));
            }

            // FPS блок и подсказка
            FpsBlock.Visibility = _fpsVisible ? Visibility.Visible : Visibility.Collapsed;
            bool nothing = !_fpsVisible && selected.Count == 0;
            EmptyHint.Visibility = nothing ? Visibility.Visible : Visibility.Collapsed;

            FpsValue.FontSize = _baseFont * 2.05;
        }

        private UIElement HeaderFor(string hwName, string hwType)
        {
            var sp = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 6)
            };

            sp.Children.Add(new Border
            {
                Width = 4,
                Height = 14,
                CornerRadius = new CornerRadius(2),
                Background = AccentFor(hwType),
                Margin = new Thickness(0, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            sp.Children.Add(new TextBlock
            {
                Text = hwName,
                FontSize = _baseFont,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xED)),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 250
            });

            return sp;
        }

        private UIElement RowFor(SensorItem item)
        {
            var g = new Grid { Margin = new Thickness(0, 1.5, 0, 1.5) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = item.Name,
                FontSize = _baseFont,
                Foreground = MutedBrush,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(label, 0);

            var value = new TextBlock
            {
                FontSize = _baseFont,
                Foreground = _valueBrush,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(10, 0, 0, 0)
            };
            BindingOperations.SetBinding(value, TextBlock.TextProperty,
                new Binding(nameof(SensorItem.Pretty)) { Source = item });
            Grid.SetColumn(value, 1);

            g.Children.Add(label);
            g.Children.Add(value);
            return g;
        }

        private static Border Divider() => new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 10, 0, 10)
        };

        private static Brush AccentFor(string hwType)
        {
            if (hwType.Contains("Gpu")) return new SolidColorBrush(Color.FromRgb(0x97, 0xC4, 0x59));
            if (hwType.Contains("Cpu")) return new SolidColorBrush(Color.FromRgb(0x5B, 0xA6, 0xE8));
            if (hwType.Contains("Memory")) return new SolidColorBrush(Color.FromRgb(0x9B, 0x92, 0xE8));
            if (hwType.Contains("Storage")) return new SolidColorBrush(Color.FromRgb(0x5D, 0xCA, 0xA5));
            if (hwType.Contains("Network")) return new SolidColorBrush(Color.FromRgb(0xE0, 0xA0, 0x7B));
            return new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        }

        // ---------- FPS ----------

        public void SetFpsVisible(bool visible)
        {
            _fpsVisible = visible;
            FpsBlock.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            Build();
        }

        public void UpdateFps(FpsStatsView v)
        {
            FpsValue.Text = v.HasData ? Math.Round(v.Current).ToString(CultureInfo.InvariantCulture) : "N/A";
            FpsAvg.Text = v.HasData ? Math.Round(v.Average).ToString(CultureInfo.InvariantCulture) : "N/A";
            FpsLow1.Text = v.HasData ? Math.Round(v.Low1).ToString(CultureInfo.InvariantCulture) : "N/A";
            FpsLow01.Text = v.HasData ? Math.Round(v.Low01).ToString(CultureInfo.InvariantCulture) : "N/A";

            DrawGraph(v.Graph);

            _dotTick++;
            LiveDot.Opacity = (v.HasData && _dotTick % 2 == 0) ? 1.0 : 0.4;
        }

        private void DrawGraph(double[] frames)
        {
            double w = GraphCanvas.ActualWidth;
            double h = GraphCanvas.ActualHeight;
            if (w <= 1 || h <= 1 || frames == null || frames.Length < 2)
            {
                GraphLine.Points = new PointCollection();
                GraphFill.Points = new PointCollection();
                return;
            }

            const double lo = 2.0;   // мс
            const double hi = 34.0;  // мс (~30 fps снизу)

            var pts = new PointCollection(frames.Length);
            for (int i = 0; i < frames.Length; i++)
            {
                double x = frames.Length == 1 ? 0 : (double)i / (frames.Length - 1) * w;
                double norm = (frames[i] - lo) / (hi - lo);
                if (norm < 0) norm = 0;
                if (norm > 1) norm = 1;
                double y = h - 1 - norm * (h - 2);
                pts.Add(new Point(x, y));
            }
            GraphLine.Points = pts;

            var fill = new PointCollection(pts.Count + 2) { new Point(0, h) };
            foreach (Point p in pts) fill.Add(p);
            fill.Add(new Point(w, h));
            GraphFill.Points = fill;
        }

        // ---------- стиль / окно ----------

        public void ApplyStyle(double fontSize, Color textColor, double backgroundOpacity)
        {
            _baseFont = fontSize;
            _valueBrush = new SolidColorBrush(textColor);
            _bgOpacity = backgroundOpacity;

            byte a = (byte)Math.Round(backgroundOpacity * 255);
            RootBorder.Background = new SolidColorBrush(Color.FromArgb(a, 0x13, 0x15, 0x1A));

            Build();
        }

        public void SetLocked(bool locked)
        {
            _locked = locked;
            if (PresentationSource.FromVisual(this) != null)
                NativeMethods.SetClickThrough(this, locked);
        }

        public void RestorePosition(double left, double top)
        {
            if (!double.IsNaN(left) && left >= 0) Left = left;
            if (!double.IsNaN(top) && top >= 0) Top = top;
        }
    }

    /// <summary>Данные для отрисовки блока FPS в оверлее.</summary>
    public readonly record struct FpsStatsView(
        bool HasData,
        double Current,
        double Average,
        double Low1,
        double Low01,
        double[] Graph);
}
