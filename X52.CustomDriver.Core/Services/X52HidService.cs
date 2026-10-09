using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HidLibrary;
using X52.CustomDriver.Core.Interfaces;
using X52.CustomDriver.Core.Models;

namespace X52.CustomDriver.Core.Services
{
    public class X52HidService : IHidService
    {
        private const int VID = 0x06A3;
        private const int PID_Pro = 0x0762;
        private const int PID_Std = 0x075C;

        private HidDevice? _device;
        private int _currentPid;
        private CancellationTokenSource? _ccts;
        private Task? _readTask;
        
        private double _lastZ = 508; 
        private const double Z_SMOOTHING_FACTOR = 0.6;

        // Auto-Calibration State
        private int _zMin = 50, _zMax = 980, _zCenter = 508;
        private int _xMin = 50, _xMax = 2000, _xCenter = 1012;
        private int _yMin = 50, _yMax = 2000, _yCenter = 1012;
        private int _lastRawX = 1012, _lastRawY = 1012, _lastRawZ = 508;

        public bool IsConnected => _device != null && _device.IsOpen;

        public string? DeviceInstanceId => DevicePathToInstanceId(_device?.DevicePath);

        // \\?\hid#vid_06a3&pid_075c#7&1a2b&0&0000#{guid}  ->  HID\VID_06A3&PID_075C\7&1A2B&0&0000
        public static string? DevicePathToInstanceId(string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string p = path;
            if (p.StartsWith(@"\\?\") || p.StartsWith(@"\\.\")) p = p.Substring(4);
            var parts = p.Split('#');
            if (parts.Length < 3) return null;
            return string.Join(@"\", parts, 0, parts.Length - 1).ToUpperInvariant();
        }

        public event EventHandler<X52State>? OnStateChanged;
        public event EventHandler<string>? OnError;
        public event EventHandler? OnDisconnected;

        public void Initialize()
        {
            if (!TryOpenDevice(out string? error))
                OnError?.Invoke(this, error ?? "X52 Device not found.");
        }

        private bool TryOpenDevice(out string? error)
        {
            error = null;
            var device = HidDevices.Enumerate(VID, PID_Pro).FirstOrDefault()
                      ?? HidDevices.Enumerate(VID, PID_Std).FirstOrDefault();
            if (device == null)
            {
                error = "X52 Device not found.";
                return false;
            }

            try
            {
                device.OpenDevice();
                _currentPid = device.Attributes.ProductId;
                _device = device;
                InitializeLeds();
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to open device: {ex.Message}";
                return false;
            }
        }

        private void HandleDisconnect()
        {
            var device = _device;
            _device = null;
            try { device?.CloseDevice(); } catch { }
            OnDisconnected?.Invoke(this, EventArgs.Empty);
        }

        // A real X52 report is never all zeros. HidLibrary hands back a zero-filled
        // buffer with "Success" when the device is unplugged mid-read.
        private static bool IsValidReport(byte[]? d)
        {
            if (d == null || d.Length < 14) return false;
            foreach (byte b in d) if (b != 0) return true;
            return false;
        }

        public void StartListening()
        {
            // Runs even when no stick is plugged in yet: the read loop connects when it appears.
            _ccts = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoop(_ccts.Token), _ccts.Token);
            _ = Task.Run(() => MfdRefreshLoop(_ccts.Token), _ccts.Token);
        }

        private async Task MfdRefreshLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try { InitializeLeds(); } catch { }
                try { await Task.Delay(2000, token); } catch { break; }
            }
        }

        public void StopListening()
        {
            _ccts?.Cancel();
            _device?.CloseDevice();
        }

        private void ReadLoop(CancellationToken token)
        {
            var lastPresenceCheck = DateTime.UtcNow;
            while (!token.IsCancellationRequested)
            {
                var device = _device;
                if (device == null || !device.IsOpen)
                {
                    // Not connected: look for the stick again every second
                    if (!TryOpenDevice(out _))
                    {
                        try { Task.Delay(1000, token).Wait(token); } catch { break; }
                    }
                    continue;
                }

                try
                {
                    var report = device.ReadReport(100);
                    var status = report?.ReadStatus ?? HidDeviceData.ReadStatus.ReadError;

                    if (status == HidDeviceData.ReadStatus.Success && IsValidReport(report!.Data))
                    {
                        var state = ParseReport(report.Data);
                        OnStateChanged?.Invoke(this, state);
                        continue;
                    }

                    // Anything other than a good report: make sure the stick is still there.
                    // (Timeouts are normal while idle, so only check those once a second.)
                    bool check = status != HidDeviceData.ReadStatus.WaitTimedOut
                              || (DateTime.UtcNow - lastPresenceCheck).TotalSeconds >= 1;
                    if (check)
                    {
                        lastPresenceCheck = DateTime.UtcNow;
                        if (!device.IsConnected)
                        {
                            HandleDisconnect();
                            continue;
                        }
                    }
                    if (status != HidDeviceData.ReadStatus.Success && status != HidDeviceData.ReadStatus.WaitTimedOut)
                        Thread.Sleep(50); // don't spin on repeated read errors
                }
                catch (Exception ex)
                {
                    OnError?.Invoke(this, $"Read Error: {ex.Message}");
                    try { if (!device.IsConnected) HandleDisconnect(); } catch { }
                    Thread.Sleep(50);
                }
            }
        }

        private X52State ParseReport(byte[] d)
        {
            var state = new X52State();
            state.Timestamp = DateTime.UtcNow;
            state.ProductId = _currentPid;
            state.RawData = (byte[])d.Clone();

            if (d == null || d.Length < 14) return state; 
            
            int rawX, rawY, rawZ;

            if (_currentPid == PID_Std)
            {
                // X52 Standard Bits (Corrected X/Y/Z)
                rawX = d[0] | ((d[1] & 0x07) << 8);
                rawY = ((d[1] & 0xF8) >> 3) | ((d[2] & 0x3F) << 5);
                rawZ = (d[2] & 0x03) | (d[3] << 2);
            }
            else
            {
                // X52 Pro Bits
                rawX = d[0] | ((d[1] & 0x07) << 8);
                rawY = ((d[1] & 0xF8) >> 3) | ((d[2] & 0x3F) << 5);
                rawZ = ((d[2] & 0xC0) >> 6) | (d[3] << 2);
            }

            _lastRawX = rawX; _lastRawY = rawY; _lastRawZ = rawZ;

            state.Throttle = (byte)(255 - d[4]);
            state.Rotary1 = d[5];
            state.Rotary2 = d[6];
            state.Slider = d[7];
            state.Pinkie = (d[8] & 0x20) != 0;

            // Calibration & Smoothing
            if (rawX < _xMin && rawX > 10) _xMin = rawX;
            if (rawX > _xMax && rawX < 2038) _xMax = rawX;
            if (rawY < _yMin && rawY > 10) _yMin = rawY;
            if (rawY > _yMax && rawY < 2038) _yMax = rawY;
            if (rawZ < _zMin && rawZ > 5) _zMin = rawZ;
            if (rawZ > _zMax && rawZ < 1018) _zMax = rawZ;

            _lastZ = (_lastZ * (1.0 - Z_SMOOTHING_FACTOR)) + (rawZ * Z_SMOOTHING_FACTOR);
            
            state.X = NormalizeAxis(rawX, _xMin, _xMax, _xCenter, 2048, 40);
            state.Y = NormalizeAxis(rawY, _yMin, _yMax, _yCenter, 2048, 40);
            state.Z = NormalizeAxis((int)_lastZ, _zMin, _zMax, _zCenter, 1024, 60); 

            // Mode detection
            int detectedMode = 1;
            if (_currentPid == PID_Pro) 
            {
                int proBits = (state.RawData[8] & 0x06) >> 1;
                detectedMode = proBits + 1;
                
                state.Trigger = (d[8] & 0x01) != 0;
                state.ButtonFire = (d[8] & 0x02) != 0;
                state.ButtonA = (d[8] & 0x04) != 0;
                state.ButtonB = (d[8] & 0x08) != 0;
                state.ButtonC = (d[8] & 0x10) != 0;
                state.Pinkie = (d[8] & 0x20) != 0;
                state.ButtonD = (d[8] & 0x40) != 0;
                state.ButtonE = (d[8] & 0x80) != 0;

                state.T1 = (d[9] & 0x01) != 0;
                state.T2 = (d[9] & 0x02) != 0;
                state.T3 = (d[9] & 0x04) != 0;
                state.T4 = (d[9] & 0x08) != 0;
                state.T5 = (d[9] & 0x10) != 0;
                state.T6 = (d[9] & 0x20) != 0;
                state.TriggerStage2 = (d[9] & 0x40) != 0;
                
                state.Hat1Up = (d[9] & 0x80) != 0;
                state.Hat1Down = (d[10] & 0x02) != 0;
                state.Hat1Left = (d[10] & 0x04) != 0;
                state.Hat1Right = (d[10] & 0x01) != 0;

                state.HatRearUp = (d[10] & 0x08) != 0;
                state.HatRearRight = (d[10] & 0x10) != 0;
                state.HatRearDown = (d[10] & 0x20) != 0;
                state.HatRearLeft = (d[10] & 0x40) != 0;

                state.MfdFunction = (d[11] & 0x04) != 0;
                state.MfdStartStop = (d[11] & 0x08) != 0;
                state.MfdReset = (d[11] & 0x10) != 0;
                state.ClutchButton = (d[11] & 0x20) != 0;
                state.MouseLeftClick = (d[11] & 0x40) != 0;
                state.MouseNubClick = (d[11] & 0x02) != 0;
                state.MouseWheelClick = (d[11] & 0x80) != 0;
            } 
            else 
            {
                // X52 Standard Mode Detection
                if ((d[11] & 0x01) != 0) detectedMode = 2;
                else if ((d[11] & 0x02) != 0) detectedMode = 3;
                else detectedMode = 1;

                state.Trigger = (d[8] & 0x01) != 0;
                state.ButtonFire = (d[8] & 0x02) != 0;
                state.ButtonA = (d[8] & 0x04) != 0;
                state.ButtonB = (d[8] & 0x08) != 0;
                state.ButtonC = (d[8] & 0x10) != 0;
                state.Pinkie = (d[8] & 0x20) != 0;
                state.ButtonD = (d[8] & 0x40) != 0;
                state.ButtonE = (d[8] & 0x80) != 0;

                state.T1 = (d[9] & 0x01) != 0;
                state.T2 = (d[9] & 0x02) != 0;
                state.T3 = (d[9] & 0x04) != 0;
                state.T4 = (d[9] & 0x08) != 0;
                state.T5 = (d[9] & 0x10) != 0;
                state.T6 = (d[9] & 0x20) != 0;
                state.TriggerStage2 = (d[9] & 0x40) != 0;
                
                state.Hat1Up = (d[9] & 0x80) != 0;
                state.Hat1Down = (d[10] & 0x02) != 0;
                state.Hat1Left = (d[10] & 0x04) != 0;
                state.Hat1Right = (d[10] & 0x01) != 0;

                state.HatRearUp = (d[10] & 0x10) != 0;
                state.HatRearRight = (d[10] & 0x08) != 0;
                state.HatRearDown = (d[10] & 0x20) != 0;
                state.HatRearLeft = (d[10] & 0x40) != 0;

                // Standard MFD and Mouse clicks (Avoiding Byte 11 bits 0 and 1 which are Modes)
                state.MfdFunction = (d[11] & 0x04) != 0;
                state.MfdStartStop = (d[11] & 0x08) != 0;
                state.MfdReset = (d[11] & 0x10) != 0;
                state.ClutchButton = (d[11] & 0x20) != 0;
                state.MouseLeftClick = (d[11] & 0x40) != 0;
                state.MouseWheelClick = (d[11] & 0x80) != 0;
                
                // --- X52 STANDARD - HAT 2 / MOUSE NUB SEPARATION (v1.1.5 SILVER BULLET) ---
                // DIAGNOSIS: 
                // - Byte 13 (Nub Y) is noisy and linked to buttons 29/30. MUST BE KILLED to stop interference.
                // - Byte 12 (Hat 2) uses discrete values (multiples of 16).
                //   Up=16, Down=80, Left=112, Right=48. Rest=0.
                // ACTION: 
                // 1. Byte 13: COMPLETELY IGNORED.
                // 2. Byte 12: Discrete exact mapping.

                int val12 = (int)d[12];

                // Precise Mapping for Hat 2
                state.Hat2Up = (val12 == 16);
                state.Hat2Down = (val12 == 80);
                state.Hat2Left = (val12 == 112);
                state.Hat2Right = (val12 == 48);

                // Internal Mouse Axes (Stick/Nub) -> FORCE TO 0 to prevent ghosting
                state.MouseX = 0;
                state.MouseY = 0;
                
                // ANNIHILATION: Force internal mouse axes to 0
                state.MouseWheelUp = false;
                state.MouseWheelDown = false;
            }
            state.CurrentMode = detectedMode;

            // Named buttons, hats and mode, decoded with the bit layout from libx52
            // (https://github.com/nirenjan/libx52). The parsing above only covered part of the
            // standard X52 and used wrong positions for most X52 Pro buttons.
            DecodeButtons(d, state);

            // Only update Mouse axes for Pro model. Standard stays zeroed to avoid interference.
            if (_currentPid == PID_Pro)
            {
                state.MouseWheelUp = (d[12] & 0x02) != 0;
                state.MouseWheelDown = (d[12] & 0x01) != 0;
                state.MouseX = (sbyte)d[13];
                state.MouseY = (sbyte)d[12];
                state.MouseNubClick = (d[11] & 0x02) != 0;
            }
            else
            {
                // Extra safety: ensure Standard model never leaks mouse data from the global section
                state.MouseX = 0;
                state.MouseY = 0;
                state.MouseWheelUp = false;
                state.MouseWheelDown = false;
            }
            
            return state;
        }

        private void DecodeButtons(byte[] d, X52State s)
        {
            bool pro = _currentPid == PID_Pro;
            if (d.Length < (pro ? 15 : 14)) return;

            ulong bits = 0;
            for (int i = 0; i < 5; i++) bits |= (ulong)d[8 + i] << (8 * i);
            bool B(int n) => ((bits >> n) & 1UL) != 0;

            // Same on both models
            s.Trigger = B(0); s.ButtonFire = B(1); s.ButtonA = B(2); s.ButtonB = B(3); s.ButtonC = B(4);
            s.Pinkie = B(5); s.ButtonD = B(6); s.ButtonE = B(7);
            s.T1 = B(8); s.T2 = B(9); s.T3 = B(10); s.T4 = B(11); s.T5 = B(12); s.T6 = B(13);
            s.TriggerStage2 = B(14);
            s.MouseNubClick = false; // the X52 has no separate thumb stick click

            if (pro)
            {
                s.MouseLeftClick = B(15); s.MouseWheelDown = B(16); s.MouseWheelUp = B(17); s.MouseWheelClick = B(18);
                s.Hat1Up = B(19); s.Hat1Right = B(20); s.Hat1Down = B(21); s.Hat1Left = B(22);
                s.HatRearUp = B(23); s.HatRearRight = B(24); s.HatRearDown = B(25); s.HatRearLeft = B(26);
                if (B(27)) s.CurrentMode = 1; else if (B(28)) s.CurrentMode = 2; else if (B(29)) s.CurrentMode = 3;
                s.ClutchButton = B(30); s.MfdFunction = B(31); s.MfdStartStop = B(32); s.MfdReset = B(33);
            }
            else
            {
                s.Hat1Up = B(15); s.Hat1Right = B(16); s.Hat1Down = B(17); s.Hat1Left = B(18);
                s.HatRearUp = B(19); s.HatRearRight = B(20); s.HatRearDown = B(21); s.HatRearLeft = B(22);
                if (B(23)) s.CurrentMode = 1; else if (B(24)) s.CurrentMode = 2; else if (B(25)) s.CurrentMode = 3;
                s.MfdFunction = B(26); s.MfdStartStop = B(27); s.MfdReset = B(28); s.ClutchButton = B(29);
                s.MouseLeftClick = B(30); s.MouseWheelClick = B(31); s.MouseWheelDown = B(32); s.MouseWheelUp = B(33);
            }

            // 8-way POV hat on top of the stick: high nibble of the byte before the thumb stick.
            // 0 = centred, 1 = up, then clockwise in 45° steps (2 = up-right ... 8 = up-left).
            int hat = d[pro ? 13 : 12] >> 4;
            s.Hat2Up = hat == 8 || hat == 1 || hat == 2;
            s.Hat2Right = hat >= 2 && hat <= 4;
            s.Hat2Down = hat >= 4 && hat <= 6;
            s.Hat2Left = hat >= 6 && hat <= 8;
        }

        public void ResetCalibration()
        {
            _xMin = 50; _xMax = 2000; _xCenter = 1012; 
            _yMin = 50; _yMax = 2000; _yCenter = 1012;
            _zMin = 50; _zMax = 980; _zCenter = 508;
            _lastZ = 508;
        }

        public void CalibrateCenter()
        {
            _xCenter = _lastRawX;
            _yCenter = _lastRawY;
            _zCenter = _lastRawZ;
        }

        public void SetLed(byte ledId, byte state)
        {
            byte[] data = new byte[8];
            data[0] = 0xB8; data[1] = ledId; data[2] = state;
            SendFeatureReport(data);
        }

        public void SetBrightness(byte brightness)
        {
            byte[] data = new byte[8];
            data[0] = 0xB1; data[1] = (byte)Math.Min(brightness, (byte)128);
            SendFeatureReport(data);
        }

        public void SetMfdText(int line, string text)
        {
            string padded = text.PadRight(16).Substring(0, 16);
            byte baseId = (byte)(0xD1 + (line * 2));
            byte[] p1 = new byte[8]; p1[0] = baseId;
            for (int i = 0; i < 7; i++) p1[i + 1] = (byte)padded[i];
            SendFeatureReport(p1);
            byte[] p2 = new byte[8]; p2[0] = (byte)(baseId + 1);
            for (int i = 0; i < 7; i++) p2[i + 1] = (byte)padded[i + 8];
            SendFeatureReport(p2);
        }

        public void SetMfdTime(int h, int m)
        {
            byte[] data = new byte[8];
            data[0] = 0xF1; data[1] = (byte)h; data[2] = (byte)m;
            SendFeatureReport(data);
        }

        private void InitializeLeds()
        {
            SetBrightness(127);
            SetMfdTime(DateTime.Now.Hour, DateTime.Now.Minute);
            SetMfdText(0, "X52 CUSTOM");
            SetMfdText(1, "READY");
        }

        private void SendFeatureReport(byte[] data)
        {
            if (!IsConnected || _device == null) return;
            try
            {
                int length = _device.Capabilities.FeatureReportByteLength;
                if (length <= 0) length = 8; 
                byte[] buffer = new byte[length];
                Array.Copy(data, 0, buffer, 0, Math.Min(data.Length, length));
                if (!_device.WriteFeatureData(buffer))
                    _device.WriteReport(new HidReport(length, new HidDeviceData(buffer, HidDeviceData.ReadStatus.Success)));
            }
            catch { }
        }

        private int ApplyDeadzone(int value, int center, int deadzone)
        {
            if (Math.Abs(value - center) < deadzone) return center;
            return value;
        }

        private int NormalizeAxis(int value, int min, int max, int center, int targetRange, int deadzone)
        {
            if (Math.Abs(value - center) < deadzone) return targetRange / 2;
            double normalized;
            if (value < center)
            {
                double range = center - min;
                if (range <= 0) return 0;
                normalized = ((double)(value - min) / range) * (targetRange / 2.0);
            }
            else
            {
                double range = max - center;
                if (range <= 0) return targetRange;
                normalized = (targetRange / 2.0) + (((double)(value - center) / range) * (targetRange / 2.0));
            }
            return Math.Clamp((int)normalized, 0, targetRange);
        }
    }
}
