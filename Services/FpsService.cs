using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FreeMon.Services
{
    /// <summary>
    /// Измеряет FPS активного приложения.
    ///
    /// Под капотом запускается PresentMon (открытый инструмент Intel, ETW),
    /// который выдаёт поток событий о выводе кадров. Мы читаем его вывод,
    /// берём колонку msBetweenPresents (время между кадрами) и считаем
    /// средний FPS за последнюю секунду.
    ///
    /// Файл PresentMon.exe должен лежать рядом с FreeMon.exe.
    /// Сборка в GitHub Actions кладёт его туда автоматически.
    /// </summary>
    public sealed class FpsService : IDisposable
    {
        private Process? _proc;
        private CancellationTokenSource? _cts;
        private readonly object _lock = new();
        private readonly LinkedList<(DateTime t, double ms)> _window = new();

        private int _frameTimeCol = -1;
        private double _fps;
        private double _frameMs;
        private int _pid = -1;

        public string? PresentMonPath { get; set; }
        public string Status { get; private set; } = "выключено";

        public bool IsAvailable =>
            !string.IsNullOrEmpty(PresentMonPath) && File.Exists(PresentMonPath);

        public bool IsRunning => _proc is { HasExited: false };
        public int CurrentPid { get { lock (_lock) return _pid; } }
        public double Fps { get { lock (_lock) return _fps; } }
        public double FrameTimeMs { get { lock (_lock) return _frameMs; } }

        /// <summary>Начать измерять FPS процесса с указанным PID.</summary>
        public void StartFor(int pid)
        {
            if (IsRunning && _pid == pid)
                return;

            Stop();

            if (!IsAvailable)
            {
                Status = "PresentMon.exe не найден рядом с программой";
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = PresentMonPath!,
                    Arguments =
                        "-process_id " + pid +
                        " -output_stdout -stop_existing_session -terminate_on_proc_exit",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _proc = Process.Start(psi);
                if (_proc == null)
                {
                    Status = "не удалось запустить PresentMon";
                    return;
                }

                lock (_lock)
                {
                    _pid = pid;
                    _window.Clear();
                    _frameTimeCol = -1;
                    _fps = 0;
                    _frameMs = 0;
                }

                _cts = new CancellationTokenSource();
                CancellationToken token = _cts.Token;
                Process proc = _proc;
                _ = Task.Run(() => ReadLoop(proc, token));

                Status = "измеряется (PID " + pid + ")";
            }
            catch (Exception ex)
            {
                Status = "ошибка: " + ex.Message;
            }
        }

        private void ReadLoop(Process proc, CancellationToken ct)
        {
            try
            {
                StreamReader reader = proc.StandardOutput;
                string? line;
                while (!ct.IsCancellationRequested && (line = reader.ReadLine()) != null)
                    ParseLine(line);
            }
            catch
            {
                // процесс завершился или поток закрыт — это нормально
            }
        }

        private void ParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            string[] parts = line.Split(',');

            // Первая значимая строка — заголовок CSV. Находим нужную колонку.
            if (_frameTimeCol < 0)
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    string h = parts[i].Trim();
                    if (h.Equals("msBetweenPresents", StringComparison.OrdinalIgnoreCase))
                    {
                        _frameTimeCol = i;
                        break;
                    }
                }
                return;
            }

            if (_frameTimeCol >= parts.Length)
                return;

            if (!double.TryParse(parts[_frameTimeCol].Trim(),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out double ms))
                return;

            // Отсекаем мусорные значения
            if (ms <= 0 || ms > 1000)
                return;

            DateTime now = DateTime.UtcNow;
            lock (_lock)
            {
                _window.AddLast((now, ms));

                DateTime cutoff = now - TimeSpan.FromMilliseconds(1000);
                while (_window.First != null && _window.First.Value.t < cutoff)
                    _window.RemoveFirst();

                double sum = 0;
                int n = 0;
                foreach (var e in _window)
                {
                    sum += e.ms;
                    n++;
                }

                if (n > 0)
                {
                    _frameMs = sum / n;
                    _fps = _frameMs > 0 ? 1000.0 / _frameMs : 0;
                }
            }
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }

            try
            {
                if (_proc is { HasExited: false })
                    _proc.Kill(true);
            }
            catch { }

            try { _proc?.Dispose(); } catch { }
            _proc = null;

            lock (_lock)
            {
                _pid = -1;
                _fps = 0;
                _frameMs = 0;
                _window.Clear();
            }

            if (Status.StartsWith("измеряется"))
                Status = "ожидание игры";
        }

        public void Dispose() => Stop();
    }
}
