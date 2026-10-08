using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace X52.CustomDriver.Core.Services
{
    /// <summary>
    /// Turns the X52 throttle thumb stick ("mouse nub") into a real Windows mouse,
    /// the way the original Saitek/Logitech driver did.
    ///
    /// Report layout (verified against libx52, https://github.com/nirenjan/libx52):
    ///   X52 (14-byte report):     thumb stick in byte 13, X = low nibble, Y = high nibble (0..15, centre 8)
    ///   X52 Pro (15-byte report): thumb stick in byte 14, same nibble layout
    ///   Buttons are a little-endian bit field starting at byte 8:
    ///     X52:     mouse primary = bit 30, secondary = 31, scroll down = 32, scroll up = 33
    ///     X52 Pro: mouse primary = bit 15, scroll down = 16, scroll up = 17, secondary = 18
    /// </summary>
    public sealed class NubMouseService : IDisposable
    {
        private const int PID_Pro = 0x0762;
        private const double NubMax = 8.0;
        private const int TickMs = 10;

        // Settings (read on every tick, so changes apply immediately)
        public volatile bool Enabled = true;
        public volatile bool ButtonsEnabled = true;
        private double _speed = 1200;     // pixels per second at full deflection
        private double _deadzone = 1.0;   // in nub units (0..8)
        public double Speed { get => Volatile.Read(ref _speed); set => Volatile.Write(ref _speed, Math.Clamp(value, 50, 6000)); }
        public double Deadzone { get => Volatile.Read(ref _deadzone); set => Volatile.Write(ref _deadzone, Math.Clamp(value, 0, 4)); }

        // Latest decoded input (written by the HID read thread)
        private volatile int _dx;
        private volatile int _dy;
        private volatile bool _hasData;

        // Last values seen, for live display in the UI
        public int RawX => _dx;
        public int RawY => _dy;

        private bool _leftDown, _rightDown, _scrollUpPrev, _scrollDownPrev;
        private readonly object _buttonLock = new();

        private readonly Thread _thread;
        private volatile bool _running = true;

        public NubMouseService()
        {
            _thread = new Thread(Loop) { IsBackground = true, Name = "NubMouse" };
            _thread.Start();
        }

        /// <summary>Feed every raw HID report here.</summary>
        public void Update(byte[]? data, int productId)
        {
            if (data == null) return;

            bool isPro = productId == PID_Pro;
            int thumbIndex = isPro ? 14 : 13;
            if (data.Length <= thumbIndex) return;

            byte thumb = data[thumbIndex];
            _dx = (thumb & 0x0F) - 8;
            _dy = (thumb >> 4) - 8;
            _hasData = true;

            ulong buttons = 0;
            for (int i = 0; i < 5 && 8 + i < data.Length; i++)
                buttons |= (ulong)data[8 + i] << (8 * i);

            bool primary, secondary, scrollDown, scrollUp;
            if (isPro)
            {
                primary = Bit(buttons, 15);
                scrollDown = Bit(buttons, 16);
                scrollUp = Bit(buttons, 17);
                secondary = Bit(buttons, 18);
            }
            else
            {
                primary = Bit(buttons, 30);
                secondary = Bit(buttons, 31);
                scrollDown = Bit(buttons, 32);
                scrollUp = Bit(buttons, 33);
            }

            HandleButtons(primary, secondary, scrollUp, scrollDown);
        }

        private static bool Bit(ulong v, int n) => (v & (1UL << n)) != 0;

        private void HandleButtons(bool primary, bool secondary, bool scrollUp, bool scrollDown)
        {
            lock (_buttonLock)
            {
                if (!Enabled || !ButtonsEnabled)
                {
                    ReleaseButtons();
                    _scrollUpPrev = scrollUp;
                    _scrollDownPrev = scrollDown;
                    return;
                }

                if (primary != _leftDown)
                {
                    SendMouse(0, 0, 0, primary ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP);
                    _leftDown = primary;
                }
                if (secondary != _rightDown)
                {
                    SendMouse(0, 0, 0, secondary ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP);
                    _rightDown = secondary;
                }
                if (scrollUp && !_scrollUpPrev) SendMouse(0, 0, WHEEL_DELTA, MOUSEEVENTF_WHEEL);
                if (scrollDown && !_scrollDownPrev) SendMouse(0, 0, unchecked((uint)-WHEEL_DELTA), MOUSEEVENTF_WHEEL);
                _scrollUpPrev = scrollUp;
                _scrollDownPrev = scrollDown;
            }
        }

        private void ReleaseButtons()
        {
            if (_leftDown) { SendMouse(0, 0, 0, MOUSEEVENTF_LEFTUP); _leftDown = false; }
            if (_rightDown) { SendMouse(0, 0, 0, MOUSEEVENTF_RIGHTUP); _rightDown = false; }
        }

        private void Loop()
        {
            var sw = Stopwatch.StartNew();
            double last = sw.Elapsed.TotalSeconds;
            double accX = 0, accY = 0;

            while (_running)
            {
                Thread.Sleep(TickMs);
                double now = sw.Elapsed.TotalSeconds;
                double dt = Math.Min(now - last, 0.05);
                last = now;

                if (!Enabled || !_hasData)
                {
                    accX = accY = 0;
                    if (!Enabled) lock (_buttonLock) ReleaseButtons();
                    continue;
                }

                int dx = _dx, dy = _dy;
                double mag = Math.Sqrt(dx * dx + dy * dy);
                double dz = Deadzone;
                if (mag <= dz)
                {
                    accX = accY = 0;
                    continue;
                }

                // Radial deadzone + 1.8 power curve (same idea as libx52's virtual mouse)
                double norm = Math.Min((mag - dz) / (NubMax - dz), 1.0);
                double pxPerSec = Speed * Math.Pow(norm, 1.8);
                accX += dx / mag * pxPerSec * dt;
                accY += dy / mag * pxPerSec * dt;

                int moveX = (int)accX, moveY = (int)accY;
                accX -= moveX;
                accY -= moveY;
                if (moveX != 0 || moveY != 0)
                    SendMouse(moveX, moveY, 0, MOUSEEVENTF_MOVE);
            }
        }

        public void Dispose()
        {
            _running = false;
            lock (_buttonLock) ReleaseButtons();
        }

        // --- Win32 SendInput ---
        private const uint INPUT_MOUSE = 0;
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const uint WHEEL_DELTA = 120;

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT { public uint type; public InputUnion u; }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private static void SendMouse(int dx, int dy, uint data, uint flags)
        {
            try
            {
                var input = new INPUT
                {
                    type = INPUT_MOUSE,
                    u = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags } }
                };
                SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
            }
            catch { /* never let mouse emulation crash the driver */ }
        }
    }
}
