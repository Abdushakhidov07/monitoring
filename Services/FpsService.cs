using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FreeMon.Services
{
    /// <summary>Сводка по FPS за окно измерения.</summary>
    public readonly record struct FpsStats(
        double Current,   // мгновенный FPS (за ~1 c)
        double Average,   // средний FPS за окно
        double Low1,      // 1% Low (FPS на 99-м перцентиле времени кадра)
        double Low01,     // 0.1% Low (99.9-й перцентиль)
        double FrameMs,   // текущее время кадра, мс
        bool HasData);

    /// <summary>
    /// Измеряет FPS активного приложения через PresentMon (открытый инструмент Intel, ETW).
    /// Читает поток времён кадров (msBetweenPresents), считает мгновенный FPS,
    /// средний, 1% и 0.1% Low, и хранит хвост значений для графика frametime.
    /// PresentMon.exe должен лежать рядом с FreeMon.exe.
    /// </summary>
    public sealed class FpsService : IDisposable
    {
        private Process? _proc;
        private CancellationTokenSource? _cts;
        private readonly object _lock = new();

        // Лог времён кадров за последние ~20 секунд: (момент, мс)
        private readonly LinkedList<(DateTime t, double ms)> _log = new();
        private static readonly TimeSpan StatsWindow = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan CurrentWindow = TimeSpan.FromSeconds(1);

        private int _frameTimeCol = -1;
        private int _pid = -1;

        public string? PresentMonPath { get; set; }
        public string Status { get; private set; } = "выключено";

        public bool IsAvailable => !string.IsNullOrEmpty(PresentMonPath) && File.Exists(PresentMonPath);
        public bool IsRunning => _proc is { HasExited: false };
        public int CurrentPid { get { lock (_lock) return _pid; } }

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
                    _log.Clear();
                    _frameTimeCol = -1;
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
                // процесс завершился — это нормально
            }
        }

        private void ParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            string[] parts = line.Split(',');

            if (_frameTimeCol < 0)
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i].Trim().Equals("msBetweenPresents", StringComparison.OrdinalIgnoreCase))
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

            if (ms <= 0 || ms > 1000)
                return;

            DateTime now = DateTime.UtcNow;
            lock (_lock)
            {
                _log.AddLast((now, ms));
                DateTime cutoff = now - StatsWindow;
                while (_log.First != null && _log.First.Value.t < cutoff)
                    _log.RemoveFirst();
            }
        }

        /// <summary>Посчитать сводку по FPS (вызывается на тике UI, не на каждом кадре).</summary>
        public FpsStats GetStats()
        {
            double[] frames;
            double currentMs;
            DateTime now = DateTime.UtcNow;

            lock (_lock)
            {
                if (_log.Count == 0)
                    return new FpsStats(0, 0, 0, 0, 0, false);

                frames = new double[_log.Count];
                int i = 0;
                double curSum = 0;
                int curN = 0;
                DateTime curCut = now - CurrentWindow;
                foreach (var e in _log)
                {
                    frames[i++] = e.ms;
                    if (e.t >= curCut) { curSum += e.ms; curN++; }
                }
                currentMs = curN > 0 ? curSum / curN : frames[frames.Length - 1];
            }

            double mean = frames.Average();
            double avgFps = mean > 0 ? 1000.0 / mean : 0;
            double current = currentMs > 0 ? 1000.0 / currentMs : 0;

            Array.Sort(frames); // по возрастанию времени кадра
            double low1 = 1000.0 / Percentile(frames, 99.0);
            double low01 = 1000.0 / Percentile(frames, 99.9);

            return new FpsStats(current, avgFps, low1, low01, currentMs, true);
        }

        private static double Percentile(double[] sortedAsc, double p)
        {
            if (sortedAsc.Length == 0) return 0;
            int idx = (int)Math.Ceiling(p / 100.0 * sortedAsc.Length) - 1;
            if (idx < 0) idx = 0;
            if (idx >= sortedAsc.Length) idx = sortedAsc.Length - 1;
            double v = sortedAsc[idx];
            return v > 0 ? v : 0.0001;
        }

        /// <summary>Последние времена кадров (мс) для графика.</summary>
        public double[] GetGraph(int count)
        {
            lock (_lock)
            {
                if (_log.Count == 0) return Array.Empty<double>();
                int take = Math.Min(count, _log.Count);
                var result = new double[take];
                int skip = _log.Count - take;
                int i = 0, j = 0;
                foreach (var e in _log)
                {
                    if (i++ < skip) continue;
                    result[j++] = e.ms;
                }
                return result;
            }
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { if (_proc is { HasExited: false }) _proc.Kill(true); } catch { }
            try { _proc?.Dispose(); } catch { }
            _proc = null;

            lock (_lock)
            {
                _pid = -1;
                _log.Clear();
            }

            if (Status.StartsWith("измеряется"))
                Status = "ожидание игры";
        }

        public void Dispose() => Stop();
    }
}
