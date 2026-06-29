using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FreeMon.Interop
{
    internal static class NativeMethods
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        /// <summary>
        /// Делает окно «прозрачным» для мыши (клики проходят сквозь него)
        /// или возвращает обычное поведение.
        /// </summary>
        public static void SetClickThrough(Window window, bool enabled)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            int style = GetWindowLong(hwnd, GWL_EXSTYLE);
            if (enabled)
                style |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
            else
                style &= ~WS_EX_TRANSPARENT;

            SetWindowLong(hwnd, GWL_EXSTYLE, style);
        }

        /// <summary>PID процесса, чьё окно сейчас в фокусе. -1, если не определить.</summary>
        public static int GetForegroundProcessId()
        {
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero)
                return -1;

            GetWindowThreadProcessId(h, out uint pid);
            return (int)pid;
        }
    }
}
