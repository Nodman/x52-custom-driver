using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;

namespace X52.CustomDriver.Core.Services
{
    public class KeyboardService
    {
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        
        [StructLayout(LayoutKind.Sequential)]
        struct HARDWAREINPUT { public uint uMsg; public ushort wParamL; public ushort wParamH; }

        const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const uint KEYEVENTF_SCANCODE = 0x0008;
        const uint MAPVK_VK_TO_VSC = 0;

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        static extern uint MapVirtualKey(uint uCode, uint uMapType);

        // Keys that need the "extended" flag when sent as scan codes
        private static readonly HashSet<ushort> ExtendedKeys = new()
        {
            0x25, 0x26, 0x27, 0x28,             // arrows
            0xA3, 0xA5,                         // right ctrl, right alt
            0x5B, 0x5C,                         // windows keys
            0x2D, 0x2E, 0x24, 0x23, 0x21, 0x22  // insert, delete, home, end, page up/down
        };

        // How many active mappings currently hold each key down (two mappings may share e.g. LSHIFT)
        private readonly Dictionary<ushort, int> _holdCount = new();
        private readonly object _lock = new();

        // Auto-repeat like a real keyboard: the last held non-modifier key repeats after a delay.
        // (Windows does not auto-repeat injected keys, so without this a held key types only once.)
        private const int RepeatDelayMs = 500;
        private const int RepeatIntervalMs = 33;
        private ushort _repeatVk;
        private DateTime _repeatSince;
        private static readonly HashSet<ushort> Modifiers = new() { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C };

        public KeyboardService()
        {
            var t = new System.Threading.Thread(RepeatLoop) { IsBackground = true, Name = "KeyRepeat" };
            t.Start();
        }

        private void RepeatLoop()
        {
            while (true)
            {
                System.Threading.Thread.Sleep(RepeatIntervalMs);
                INPUT? repeat = null;
                lock (_lock)
                {
                    if (_repeatVk != 0 && _holdCount.ContainsKey(_repeatVk) &&
                        (DateTime.UtcNow - _repeatSince).TotalMilliseconds >= RepeatDelayMs)
                        repeat = MakeInput(_repeatVk, up: false);
                }
                if (repeat.HasValue) Send(new List<INPUT> { repeat.Value });
            }
        }

        /// <summary>Keys currently held down by mappings, for display.</summary>
        public string HeldKeysText
        {
            get
            {
                lock (_lock)
                {
                    if (_holdCount.Count == 0) return "";
                    var names = _holdCount.Keys.Select(vk => KeyMap.FirstOrDefault(kv => kv.Value == vk).Key ?? $"0x{vk:X2}");
                    return string.Join(" + ", names);
                }
            }
        }

        // Map of friendly names to Virtual Key codes
        private static readonly Dictionary<string, ushort> KeyMap = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            // Modifiers
            { "LSHIFT", 0xA0 }, { "RSHIFT", 0xA1 }, { "LCTRL", 0xA2 }, { "RCTRL", 0xA3 }, { "LALT", 0xA4 }, { "RALT", 0xA5 },
            // Letters A-Z
            { "A", 0x41 }, { "B", 0x42 }, { "C", 0x43 }, { "D", 0x44 }, { "E", 0x45 }, { "F", 0x46 }, { "G", 0x47 }, { "H", 0x48 }, 
            { "I", 0x49 }, { "J", 0x4A }, { "K", 0x4B }, { "L", 0x4C }, { "M", 0x4D }, { "N", 0x4E }, { "O", 0x4F }, { "P", 0x50 }, 
            { "Q", 0x51 }, { "R", 0x52 }, { "S", 0x53 }, { "T", 0x54 }, { "U", 0x55 }, { "V", 0x56 }, { "W", 0x57 }, { "X", 0x58 }, 
            { "Y", 0x59 }, { "Z", 0x5A },
            // Digits 0-9
            { "0", 0x30 }, { "1", 0x31 }, { "2", 0x32 }, { "3", 0x33 }, { "4", 0x34 }, { "5", 0x35 }, { "6", 0x36 }, { "7", 0x37 }, { "8", 0x38 }, { "9", 0x39 },
            // Function Keys F1-F12
            { "F1", 0x70 }, { "F2", 0x71 }, { "F3", 0x72 }, { "F4", 0x73 }, { "F5", 0x74 }, { "F6", 0x75 }, { "F7", 0x76 }, { "F8", 0x77 }, { "F9", 0x78 }, { "F10", 0x79 }, { "F11", 0x7A }, { "F12", 0x7B },
            // Arrows
            { "UP", 0x26 }, { "DOWN", 0x28 }, { "LEFT", 0x25 }, { "RIGHT", 0x27 },
            // Common
            { "SPACE", 0x20 }, { "ENTER", 0x0D }, { "ESCAPE", 0x1B }, { "TAB", 0x09 }, { "BACKSPACE", 0x08 }, { "LWIN", 0x5B }, { "RWIN", 0x5C },
            { "DELETE", 0x2E }, { "INSERT", 0x2D }, { "HOME", 0x24 }, { "END", 0x23 }, { "PAGEUP", 0x21 }, { "PAGEDOWN", 0x22 }
        };

        private static List<ushort> Resolve(IEnumerable<string> keys) =>
            keys.Select(k => KeyMap.TryGetValue(k, out var vk) ? vk : (ushort)0).Where(v => v != 0).ToList();

        /// <summary>Press keys and keep them held (e.g. while a stick button is held).</summary>
        public void Press(IEnumerable<string> keys)
        {
            var toSend = new List<INPUT>();
            lock (_lock)
            {
                foreach (var vk in Resolve(keys))
                {
                    _holdCount.TryGetValue(vk, out int n);
                    _holdCount[vk] = n + 1;
                    if (n == 0) toSend.Add(MakeInput(vk, up: false));
                    if (!Modifiers.Contains(vk)) { _repeatVk = vk; _repeatSince = DateTime.UtcNow; }
                }
            }
            Send(toSend);
        }

        /// <summary>Release keys pressed with <see cref="Press"/>, in reverse order.</summary>
        public void Release(IEnumerable<string> keys)
        {
            var toSend = new List<INPUT>();
            lock (_lock)
            {
                foreach (var vk in Resolve(keys).AsEnumerable().Reverse())
                {
                    if (!_holdCount.TryGetValue(vk, out int n) || n <= 0) continue;
                    if (n == 1)
                    {
                        _holdCount.Remove(vk);
                        toSend.Add(MakeInput(vk, up: true));
                        if (_repeatVk == vk) _repeatVk = 0;
                    }
                    else _holdCount[vk] = n - 1;
                }
            }
            Send(toSend);
        }

        /// <summary>Short press. Held ~50 ms so games that poll once per frame still see it.</summary>
        public void Tap(List<string> keys)
        {
            var copy = keys.ToList();
            Press(copy);
            System.Threading.Tasks.Task.Delay(50).ContinueWith(_ => Release(copy));
        }

        /// <summary>Let go of everything this service is holding (unplug, profile switch, exit).</summary>
        public void ReleaseAll()
        {
            var toSend = new List<INPUT>();
            lock (_lock)
            {
                foreach (var vk in _holdCount.Keys) toSend.Add(MakeInput(vk, up: true));
                _holdCount.Clear();
                _repeatVk = 0;
            }
            Send(toSend);
        }

        // Kept for compatibility: a single tap of the whole combination
        public void SendKeys(List<string> keys) => Tap(keys);

        // Scan codes work in games that read DirectInput/raw input as well as in normal apps
        private static INPUT MakeInput(ushort vk, bool up)
        {
            uint scan = MapVirtualKey(vk, MAPVK_VK_TO_VSC);
            var ki = new KEYBDINPUT();
            if (scan != 0)
            {
                ki.wScan = (ushort)scan;
                ki.dwFlags = KEYEVENTF_SCANCODE | (ExtendedKeys.Contains(vk) ? KEYEVENTF_EXTENDEDKEY : 0);
            }
            else
            {
                ki.wVk = vk;
            }
            if (up) ki.dwFlags |= KEYEVENTF_KEYUP;
            return new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = ki } };
        }

        private static void Send(List<INPUT> inputs)
        {
            if (inputs.Count == 0) return;
            try { SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT))); }
            catch { /* never crash the driver over a key event */ }
        }
    }
}
