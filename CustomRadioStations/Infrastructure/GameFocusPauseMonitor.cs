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
        private bool escapeWasDown;
        private uint startButtonMask;
        private bool xinputUnavailable;
        private bool disposed;

        private const int VirtualKeyEscape = 0x1b;
        private const ushort XInputGamepadStart = 0x0010;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("xinput1_4.dll")]
        private static extern int XInputGetState(uint userIndex, out XInputState state);

        internal GameFocusPauseMonitor() {
            timer = new Timer(Poll, null, 0, 100);
        }

        private void Poll(object state) {
            if (disposed)
                return;

            try {
                IntPtr gameWindow = Process.GetCurrentProcess().MainWindowHandle;
                if (gameWindow == IntPtr.Zero)
                    return;

                bool isForeground = GetForegroundWindow() == gameWindow;
                if (wasForeground != isForeground) {
                    wasForeground = isForeground;
                    AudioPauseCoordinator.SetFocusPaused(!isForeground);
                }

                PollPauseButtons(isForeground);
            } catch {
                // Focus monitoring is best-effort and must never take down the script.
            }
        }

        private void PollPauseButtons(bool isForeground) {
            bool escapeDown = (GetAsyncKeyState(VirtualKeyEscape) & 0x8000) != 0;
            uint currentStartButtonMask = GetStartButtonMask();
            bool pausePressed = (!escapeWasDown && escapeDown) ||
                (currentStartButtonMask & ~startButtonMask) != 0;
            escapeWasDown = escapeDown;
            startButtonMask = currentStartButtonMask;

            if (isForeground && pausePressed)
                AudioPauseCoordinator.NotifyPauseInput();
        }

        private uint GetStartButtonMask() {
            if (xinputUnavailable)
                return 0;

            uint mask = 0;
            try {
                for (uint index = 0; index < 4; index++) {
                    XInputState state;
                    if (XInputGetState(index, out state) == 0 &&
                        (state.Gamepad.Buttons & XInputGamepadStart) != 0) {
                        mask |= 1u << (int)index;
                    }
                }
            } catch (DllNotFoundException) {
                xinputUnavailable = true;
            } catch (EntryPointNotFoundException) {
                xinputUnavailable = true;
            }
            return mask;
        }

        public void Dispose() {
            if (disposed)
                return;
            disposed = true;
            timer.Dispose();
            AudioPauseCoordinator.SetFocusPaused(false);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputState {
            internal uint PacketNumber;
            internal XInputGamepad Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputGamepad {
            internal ushort Buttons;
            internal byte LeftTrigger;
            internal byte RightTrigger;
            internal short ThumbLX;
            internal short ThumbLY;
            internal short ThumbRX;
            internal short ThumbRY;
        }
    }
}
