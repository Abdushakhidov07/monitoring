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

        private SensorItem _fpsItem = null!;
        private SensorItem _frameItem = null!;

        private bool _fpsEnabled;
        private bool _loaded;

        private static readonly string[] ColorNames =
            { "White", "Lime", "Cyan", "Yellow", "Orange", "Magenta", "Red", "LightGray" };

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Closing += OnClosing;
        }

        // ---------- жизненный цикл ----------

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            foreach (string name in ColorNames)
                ColorBox.Items.Add(name);

            bool fresh = !ConfigService.ConfigExists();

            BuildSensorList();
            AddFpsItems();

            _view = CollectionViewSource.GetDefaultView(_all);
            _view.Filter = FilterSensor;
            SensorGrid.ItemsSource = _view;

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

        private void AddFpsItems()
        {
            _fpsItem = new SensorItem("fps/overall", "Игра (активное окно)", "FPS",
                                      "FPS", SensorType.Load, ValueKind.Fps);
            _frameItem = new SensorItem("fps/frametime", "Игра (активное окно)", "FPS",
                                        "Frametime", SensorType.Load, ValueKind.FrameTime);

            foreach (SensorItem it in new[] { _fpsItem, _frameItem })
            {
                it.PropertyChanged += Item_PropertyChanged;
                _byId[it.Id] = it;
                _all.Add(it);
            }
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

                double fps = _fps.Fps;
                double ft = _fps.FrameTimeMs;
                _fpsItem.Value = fps > 0 ? fps : (double?)null;
                _frameItem.Value = ft > 0 ? ft : (double?)null;
                FpsStatus.Text = _fps.Status;
            }
            else
            {
                _fpsItem.Value = null;
                _frameItem.Value = null;
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

        private void IntervalBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(IntervalBox.Text, out int ms))
            {
                ms = Math.Max(200, Math.Min(10000, ms));
                IntervalBox.Text = ms.ToString();
                _timer.Interval = TimeSpan.FromMilliseconds(ms);
                if (_loaded) SaveConfig();
            }
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

            if (_loaded) SaveConfig();
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

            Color color = ParseColor((string)(ColorBox.SelectedItem ?? "Lime"));
            _overlay.ApplyStyle(FontSizeSlider.Value, color, BgOpacitySlider.Value);
        }

        private void OverlayStyleChanged(object sender, EventArgs e)
        {
            if (!_loaded)
                return;

            ApplyOverlayStyle();
            SaveConfig();
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
            return Colors.Lime;
        }

        // ---------- настройки ----------

        private void PreselectDefaults()
        {
            void Pick(Func<SensorItem, bool> predicate)
            {
                SensorItem? s = _all.FirstOrDefault(predicate);
                if (s != null)
                    s.IsSelected = true;
            }

            Pick(s => s.HardwareType.Contains("Cpu") && s.SensorType == SensorType.Temperature);
            Pick(s => s.HardwareType.Contains("Cpu") && s.SensorType == SensorType.Load
                      && s.Name.Contains("Total"));
            Pick(s => s.HardwareType.Contains("Gpu") && s.SensorType == SensorType.Temperature);
            Pick(s => s.HardwareType.Contains("Gpu") && s.SensorType == SensorType.Load);
            Pick(s => s.HardwareType.Contains("Memory") && s.SensorType == SensorType.Load);
        }

        private void ApplyConfig()
        {
            IntervalBox.Text = _config.IntervalMs.ToString();
            FontSizeSlider.Value = _config.FontSize;
            BgOpacitySlider.Value = _config.BackgroundOpacity;

            ColorBox.SelectedItem = _config.TextColor;
            if (ColorBox.SelectedItem == null)
                ColorBox.SelectedItem = "Lime";

            LockCheck.IsChecked = _config.Locked;
            FpsCheck.IsChecked = _config.FpsEnabled;

            var set = new HashSet<string>(_config.SelectedSensorIds);
            foreach (SensorItem s in _all)
                s.IsSelected = set.Contains(s.Id);
        }

        private void SaveConfig()
        {
            _config.SelectedSensorIds = _all.Where(s => s.IsSelected).Select(s => s.Id).ToList();

            if (int.TryParse(IntervalBox.Text, out int ms))
                _config.IntervalMs = ms;

            _config.FontSize = FontSizeSlider.Value;
            _config.BackgroundOpacity = BgOpacitySlider.Value;
            _config.TextColor = (string)(ColorBox.SelectedItem ?? "Lime");
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
