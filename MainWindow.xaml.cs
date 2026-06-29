using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FreeMon.Interop;
using FreeMon.Models;
using FreeMon.Services;
using LibreHardwareMonitor.Hardware;

namespace FreeMon
{
    public partial class MainWindow : Window
    {
        private readonly MonitorService _monitor = new();
        private readonly FpsService _fps = new();

        private readonly Dictionary<string, SensorItem> _byId = new();
        private readonly ObservableCollection<SensorItem> _all = new();
        private ICollectionView _view = null!;

        private readonly DispatcherTimer _timer = new();
        private readonly Stopwatch _fgWatch = Stopwatch.StartNew();

        private OverlayWindow? _overlay;
        private AppConfig _config = new();

        private bool _fpsEnabled;
        private bool _loaded;

        private readonly List<Border> _swatches = new();
        private string _selectedColorName = "White";

        private static readonly string[] ColorNames =
            { "White", "Lime", "Cyan", "Yellow", "Orange", "Magenta", "Red", "LightGray" };

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Closing += OnClosing;
        }

        // ---------- жизненный цикл ----------

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            bool fresh = !ConfigService.ConfigExists();

            BuildSensorList();
            BuildSwatches();

            _view = CollectionViewSource.GetDefaultView(_all);
            _view.Filter = FilterSensor;
            if (_view.GroupDescriptions.Count == 0)
                _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SensorItem.HardwareName)));
            SensorList.ItemsSource = _view;

            _config = ConfigService.Load();
            ApplyConfig();

            if (fresh)
            {
                PreselectDefaults();
                SaveConfig();
            }

            _timer.Tick += (_, _) => UpdateValues();
            _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(200, _config.IntervalMs));
            _timer.Start();

            UpdateValues();
            _loaded = true;
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            SaveConfig();
            _timer.Stop();
            _fps.Dispose();
            _overlay?.Close();
            _monitor.Dispose();
        }

        // ---------- датчики ----------

        private void BuildSensorList()
        {
            _monitor.Update();
            AddOrUpdate();
        }

        private void AddOrUpdate()
        {
            foreach (SensorReading r in _monitor.ReadAll())
            {
                if (_byId.TryGetValue(r.Id, out SensorItem? item))
                {
                    item.Value = r.Value;
                }
                else
                {
                    var it = new SensorItem(r.Id, r.HardwareName, r.HardwareType, r.Name, r.Type)
                    {
                        Value = r.Value
                    };
                    it.PropertyChanged += Item_PropertyChanged;
                    _byId[r.Id] = it;
                    _all.Add(it);
                }
            }
        }

        private void UpdateValues()
        {
            try
            {
                _monitor.Update();
                AddOrUpdate();
            }
            catch
            {
                // редкие сбои чтения датчика игнорируем
            }

            UpdateFps();
        }

        private void UpdateFps()
        {
            if (_fpsEnabled)
            {
                if (_fgWatch.ElapsedMilliseconds > 1000)
                {
                    _fgWatch.Restart();
                    int pid = NativeMethods.GetForegroundProcessId();
                    int self = Environment.ProcessId;
                    if (pid > 0 && pid != self)
                        _fps.StartFor(pid);
                }

                FpsStats st = _fps.GetStats();
                double[] graph = _fps.GetGraph(80);

                FpsStatus.Text = st.HasData
                    ? Math.Round(st.Current) + " FPS · 1% " + Math.Round(st.Low1) + " · " + _fps.Status
                    : _fps.Status;

                _overlay?.UpdateFps(new FpsStatsView(
                    st.HasData, st.Current, st.Average, st.Low1, st.Low01, graph));
            }
            else
            {
                FpsStatus.Text = "FPS выключен";
            }
        }

        // ---------- фильтр поиска ----------

        private bool FilterSensor(object obj)
        {
            if (obj is not SensorItem s)
                return false;

            string? q = SearchBox.Text?.Trim();
            if (string.IsNullOrEmpty(q))
                return true;

            return s.HardwareName.Contains(q, StringComparison.OrdinalIgnoreCase)
                || s.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || s.TypeName.Contains(q, StringComparison.OrdinalIgnoreCase)
                || s.HardwareType.Contains(q, StringComparison.OrdinalIgnoreCase);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
            => _view?.Refresh();

        // ---------- реакция на изменения ----------

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!_loaded)
                return;

            if (e.PropertyName == nameof(SensorItem.IsSelected))
            {
                _overlay?.RefreshList();
                SaveConfig();
            }
        }

        private void ClearSelection_Click(object sender, RoutedEventArgs e)
        {
            foreach (SensorItem s in _all)
                s.IsSelected = false;

            _overlay?.RefreshList();
            SaveConfig();
        }

        // ---------- FPS ----------

        private void FpsCheck_Changed(object sender, RoutedEventArgs e)
        {
            _fpsEnabled = FpsCheck.IsChecked == true;

            if (_fpsEnabled)
            {
                _fps.PresentMonPath = Path.Combine(AppContext.BaseDirectory, "PresentMon.exe");
                if (!_fps.IsAvailable)
                    FpsStatus.Text = "PresentMon.exe не найден рядом с программой";
            }
            else
            {
                _fps.Stop();
            }

            _overlay?.SetFpsVisible(_fpsEnabled);

            if (_loaded) SaveConfig();
        }

        // ---------- слайдеры оформления ----------

        private void OverlayStyleChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (FontVal != null && FontSizeSlider != null)
                FontVal.Text = ((int)Math.Round(FontSizeSlider.Value)).ToString();
            if (OpacityVal != null && BgOpacitySlider != null)
                OpacityVal.Text = ((int)Math.Round(BgOpacitySlider.Value * 100)) + "%";

            if (!_loaded) return;
            ApplyOverlayStyle();
            SaveConfig();
        }

        private void IntervalSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            int ms = (int)Math.Round(IntervalSlider.Value / 50.0) * 50;
            if (IntervalVal != null)
                IntervalVal.Text = ms + " мс";

            if (!_loaded) return;
            _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(200, ms));
            SaveConfig();
        }

        // ---------- цвет ----------

        private void BuildSwatches()
        {
            ColorSwatches.Children.Clear();
            _swatches.Clear();

            foreach (string name in ColorNames)
            {
                var b = new Border
                {
                    Width = 24,
                    Height = 24,
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(ParseColor(name)),
                    Margin = new Thickness(0, 0, 8, 8),
                    Cursor = Cursors.Hand,
                    BorderBrush = Brushes.Transparent,
                    BorderThickness = new Thickness(2),
                    Tag = name,
                    ToolTip = name
                };
                b.MouseLeftButtonUp += Swatch_Click;
                _swatches.Add(b);
                ColorSwatches.Children.Add(b);
            }
        }

        private void Swatch_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b && b.Tag is string name)
            {
                _selectedColorName = name;
                HighlightSwatch(name);
                if (_loaded)
                {
                    ApplyOverlayStyle();
                    SaveConfig();
                }
            }
        }

        private void HighlightSwatch(string name)
        {
            Brush ring = new SolidColorBrush(Color.FromRgb(0x4C, 0x8F, 0xD6));
            foreach (Border b in _swatches)
                b.BorderBrush = (b.Tag as string) == name ? ring : Brushes.Transparent;
        }

        // ---------- оверлей ----------

        private void ToggleOverlayBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_overlay is null)
            {
                _overlay = new OverlayWindow(_all);
                _overlay.Closed += (_, _) =>
                {
                    _overlay = null;
                    ToggleOverlayBtn.Content = "Показать оверлей";
                };

                _overlay.Show();
                ApplyOverlayStyle();
                _overlay.SetFpsVisible(_fpsEnabled);
                _overlay.RestorePosition(_config.OverlayLeft, _config.OverlayTop);
                _overlay.SetLocked(LockCheck.IsChecked == true);
                _overlay.RefreshList();

                ToggleOverlayBtn.Content = "Скрыть оверлей";
            }
            else
            {
                _config.OverlayLeft = _overlay.Left;
                _config.OverlayTop = _overlay.Top;
                _overlay.Close();
                SaveConfig();
            }
        }

        private void ApplyOverlayStyle()
        {
            if (_overlay is null)
                return;

            Color color = ParseColor(_selectedColorName);
            _overlay.ApplyStyle(FontSizeSlider.Value, color, BgOpacitySlider.Value);
        }

        private void LockCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (!_loaded)
                return;

            _overlay?.SetLocked(LockCheck.IsChecked == true);
            SaveConfig();
        }

        private static Color ParseColor(string name)
        {
            try
            {
                object? c = ColorConverter.ConvertFromString(name);
                if (c is Color color)
                    return color;
            }
            catch { }
            return Colors.White;
        }

        // ---------- настройки ----------

        private void PreselectDefaults()
        {
            void Sel(string hw, SensorType t, int count = 1, string? prefer = null)
            {
                int c = 0;

                if (prefer != null)
                {
                    SensorItem? p = _all.FirstOrDefault(s =>
                        s.HardwareType.Contains(hw) && s.SensorType == t && s.Name.Contains(prefer));
                    if (p != null) { p.IsSelected = true; c++; }
                }

                foreach (SensorItem s in _all)
                {
                    if (c >= count) break;
                    if (s.HardwareType.Contains(hw) && s.SensorType == t && !s.IsSelected)
                    {
                        s.IsSelected = true;
                        c++;
                    }
                }
            }

            // Видеокарта
            Sel("Gpu", SensorType.Load);
            Sel("Gpu", SensorType.Temperature);
            Sel("Gpu", SensorType.Clock, 2);
            Sel("Gpu", SensorType.Power);
            Sel("Gpu", SensorType.SmallData);

            // Процессор
            Sel("Cpu", SensorType.Load, 1, "Total");
            Sel("Cpu", SensorType.Temperature, 1, "Package");
            Sel("Cpu", SensorType.Clock, 2);
            Sel("Cpu", SensorType.Power, 1, "Package");

            // Оперативная память
            Sel("Memory", SensorType.Data, 1, "Used");
            Sel("Memory", SensorType.Load);
        }

        private void ApplyConfig()
        {
            FontSizeSlider.Value = Clamp(_config.FontSize, 11, 24);
            BgOpacitySlider.Value = Clamp(_config.BackgroundOpacity, 0.2, 1.0);
            IntervalSlider.Value = Clamp(_config.IntervalMs, 200, 2000);

            _selectedColorName = ColorNames.Contains(_config.TextColor) ? _config.TextColor : "White";
            HighlightSwatch(_selectedColorName);

            LockCheck.IsChecked = _config.Locked;
            FpsCheck.IsChecked = _config.FpsEnabled;

            FontVal.Text = ((int)Math.Round(FontSizeSlider.Value)).ToString();
            OpacityVal.Text = ((int)Math.Round(BgOpacitySlider.Value * 100)) + "%";
            IntervalVal.Text = ((int)Math.Round(IntervalSlider.Value)) + " мс";

            var set = new HashSet<string>(_config.SelectedSensorIds);
            foreach (SensorItem s in _all)
                s.IsSelected = set.Contains(s.Id);
        }

        private static double Clamp(double v, double lo, double hi)
            => v < lo ? lo : (v > hi ? hi : v);

        private void SaveConfig()
        {
            _config.SelectedSensorIds = _all.Where(s => s.IsSelected).Select(s => s.Id).ToList();
            _config.IntervalMs = (int)Math.Round(IntervalSlider.Value);
            _config.FontSize = FontSizeSlider.Value;
            _config.BackgroundOpacity = BgOpacitySlider.Value;
            _config.TextColor = _selectedColorName;
            _config.Locked = LockCheck.IsChecked == true;
            _config.FpsEnabled = FpsCheck.IsChecked == true;

            if (_overlay != null)
            {
                _config.OverlayLeft = _overlay.Left;
                _config.OverlayTop = _overlay.Top;
            }

            ConfigService.Save(_config);
        }
    }
}
