using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace CustomRadioStations {
    /// <summary>
    /// GTA suspends script ticks when pause-on-focus-loss activates, while external
    /// audio continues on its own thread. This process-level monitor therefore cannot
    /// depend on a later game tick to observe an alt-tab.
    /// </summary>
    internal sealed class GameFocusPauseMonitor : IDisposable {
        private readonly Timer timer;
        private bool? wasForeground;
        private bool disposed;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        internal GameFocusPauseMonitor() {
            timer = new Timer(Poll, null, 0, 100);
        }

        private void Poll(object state) {
            if (disposed) return;

            try {
                IntPtr gameWindow = Process.GetCurrentProcess().MainWindowHandle;
                if (gameWindow == IntPtr.Zero) return;

                bool isForeground = GetForegroundWindow() == gameWindow;
                if (wasForeground == isForeground) return;
                wasForeground = isForeground;
                AudioPauseCoordinator.SetFocusPaused(!isForeground);
            } catch {
                // Focus monitoring is best-effort and must never take down the script.
            }
        }

        public void Dispose() {
            if (disposed) return;
            disposed = true;
            timer.Dispose();
            AudioPauseCoordinator.SetFocusPaused(false);
        }
    }
}
