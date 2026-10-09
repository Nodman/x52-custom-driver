using System;
using System.Reflection;
using System.Linq;
using System.Runtime.InteropServices;
using X52.CustomDriver.Core.Interfaces;

namespace X52.CustomDriver.Core.Services
{
    public class VJoyService : IVJoyService
    {
        private object? _manager;
        private object? _joyController;
        private MethodInfo? _acquireMethod;
        private MethodInfo? _relinquishMethod;
        
        // Axis Methods Cache
        private MethodInfo? _setAxisXMethod;
        private MethodInfo? _setAxisYMethod;
        private MethodInfo? _setAxisZMethod;
        private MethodInfo? _setRxMethod;
        private MethodInfo? _setRyMethod;
        private MethodInfo? _setRzMethod;

        private MethodInfo? _setSlider0Method;
        private MethodInfo? _setSlider1Method;
        private MethodInfo? _setDialMethod;

        // Direct calls into vJoyInterface.dll (the same DLL the wrapper uses) for things the
        // wrapper doesn't expose: POV hats and the configured button/POV counts.
        [DllImport("vJoyInterface.dll", EntryPoint = "GetVJDButtonNumber")] private static extern int NativeButtonNumber(uint rID);
        [DllImport("vJoyInterface.dll", EntryPoint = "GetVJDContPovNumber")] private static extern int NativeContPovNumber(uint rID);
        [DllImport("vJoyInterface.dll", EntryPoint = "GetVJDDiscPovNumber")] private static extern int NativeDiscPovNumber(uint rID);
        [DllImport("vJoyInterface.dll", EntryPoint = "SetContPov")] private static extern bool NativeSetContPov(int value, uint rID, uint nPov);
        [DllImport("vJoyInterface.dll", EntryPoint = "SetDiscPov")] private static extern bool NativeSetDiscPov(int value, uint rID, uint nPov);

        [DllImport("vJoyInterface.dll", EntryPoint = "GetVJDStatus")] private static extern int NativeGetStatus(uint rID);

        public const int StatusOwn = 0, StatusFree = 1, StatusBusy = 2, StatusMissing = 3, StatusUnknown = 4;

        public int QueryDeviceStatus(uint id)
        {
            try
            {
                int s = NativeGetStatus(id);
                return s is >= StatusOwn and <= StatusUnknown ? s : StatusUnknown;
            }
            catch { return StatusUnknown; }
        }

        public (int buttons, int contPovs, int discPovs) QueryDeviceLayout(uint id)
        {
            try { return (NativeButtonNumber(id), NativeContPovNumber(id), NativeDiscPovNumber(id)); }
            catch { return (0, 0, 0); }
        }

        public int ButtonCount { get; private set; }
        public int ContinuousPovCount { get; private set; }
        public int DiscretePovCount { get; private set; }
        private int _lastPov = int.MinValue;

        public void SetPov(int direction)
        {
            if (!IsAvailable || direction == _lastPov) return;
            _lastPov = direction;
            try
            {
                if (ContinuousPovCount > 0)
                {
                    // Hundredths of a degree, -1 = centred
                    NativeSetContPov(direction < 0 ? -1 : direction * 100, DeviceId, 1);
                }
                else if (DiscretePovCount > 0)
                {
                    // 4-way hat: 0 up, 1 right, 2 down, 3 left; diagonals fall back to the
                    // cardinal direction just before them (counter-clockwise)
                    int v = direction < 0 ? -1 : (direction / 90) % 4;
                    NativeSetDiscPov(v, DeviceId, 1);
                }
            }
            catch (Exception ex) { Console.WriteLine($"[VJoyService] SetPov failed: {ex.Message}"); }
        }

        // Name shown in Game Controllers and games. Windows keeps one name for VID 1234 / PID BEAD,
        // so it applies to every vJoy device. Installing or reconfiguring vJoy (vJoyConfig) makes
        // Windows rebuild the entry, either empty or as the default "vJoy Device": put ours back then.
        public const string BrandName = "Ærakon X52 Virtual Joystick";
        private const string OemKey = @"System\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM\VID_1234&PID_BEAD";
        public void EnsureBrandName()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(OemKey);
                string? current = key?.GetValue("OEMName") as string;
                if (string.IsNullOrEmpty(current) || current.Trim().Equals("vJoy Device", StringComparison.OrdinalIgnoreCase))
                    key?.SetValue("OEMName", BrandName);
            }
            catch { /* cosmetic only */ }
        }

        private void ReadCapabilities()
        {
            try
            {
                ButtonCount = NativeButtonNumber(DeviceId);
                ContinuousPovCount = NativeContPovNumber(DeviceId);
                DiscretePovCount = NativeDiscPovNumber(DeviceId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VJoyService] Could not read vJoy capabilities: {ex.Message}");
                ButtonCount = 32; // assume the common default
            }
        }

        public bool IsAvailable { get; private set; }
        public uint DeviceId { get; private set; }
        public string DeviceName 
        {
            get
            {
                try
                {
                    // vJoy VID/PID is 1234/BEAD. Windows stores the name in the registry.
                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"System\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM\VID_1234&PID_BEAD");
                    var name = key?.GetValue("OEMName") as string;
                    if (!string.IsNullOrEmpty(name)) return $"{name}  (vJoy device #{DeviceId})";
                }
                catch { }
                return $"vJoy device #{DeviceId}";
            }
        }

        public bool Initialize(uint deviceId)
        {
            DeviceId = deviceId;
            _lastPov = int.MinValue;
            try
            {
                // Path to NuGet package DLL - make this relative or config driven in production
                // For now, hardcoded to standard location or local copy if we move it.
                // Better approach: Look for it in AppDomain base directory first.
                string dllName = "CoreDX.vJoy.Wrapper.dll";
                string dllPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dllName);
                
                // Fallback to absolute path used in PoC if local not found (temp hack for dev environment)
                if (!System.IO.File.Exists(dllPath))
                {
                    dllPath = @"c:\Users\redro\.nuget\packages\coredx.vjoy.wrapper\1.2.3\lib\net5.0\CoreDX.vJoy.Wrapper.dll";
                }

                if (!System.IO.File.Exists(dllPath))
                {
                    Console.WriteLine($"[VJoyService] DLL not found at {dllPath}");
                    return false;
                }

                var assembly = Assembly.LoadFrom(dllPath);
                var managerType = assembly.GetType("CoreDX.vJoy.Wrapper.VJoyControllerManager");
                
                if (managerType != null)
                {
                    var getManagerMethod = managerType.GetMethod("GetManager");
                    _manager = getManagerMethod?.Invoke(null, null);
                    
                    if (_manager != null)
                    {
                        var enabledProp = managerType.GetProperty("IsVJoyEnabled"); 
                        bool enabled = (bool)(enabledProp?.GetValue(_manager) ?? false);
                        
                        if (enabled)
                        {
                            _acquireMethod = managerType.GetMethod("AcquireController");
                            _relinquishMethod = managerType.GetMethod("RelinquishController");
                            
                            if (_acquireMethod != null)
                            {
                                 _joyController = _acquireMethod.Invoke(_manager, new object[] { DeviceId });
                                 
                                 if (_joyController != null)
                                 {
                                     // The original instruction had a problematic line `_joyController = Activator.CreateInstance(type);`
                                     // and used `type` (which is managerType) for GetMethods.
                                     // To faithfully log methods of the *acquired controller* as intended by the instruction's goal,
                                     // we use _joyController.GetType() here.
                                     var controllerType = _joyController.GetType();
                                     Console.WriteLine("[DIAG] vJoy Controller Methods:");
                                     foreach (var method in controllerType.GetMethods())
                                     {
                                         if (method.Name.StartsWith("Set") || method.Name.Contains("Axis") || method.Name.Contains("Slider") || method.Name.Contains("Dial"))
                                             Console.WriteLine($"  -> {method.Name}");
                                     }
                                     
                                     CacheMethods();
                                     Reset();
                                     ReadCapabilities();
                                     EnsureBrandName();
                                     IsAvailable = true;
                                     Console.WriteLine($"[VJoyService] Device {DeviceId} Acquired.");
                                     return true;
                                 }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VJoyService] Initialization failed: {ex.Message}");
            }

            IsAvailable = false;
            return false;
        }

        private void CacheMethods()
        {
            if (_joyController == null) return;
            var type = _joyController.GetType();
            _setAxisXMethod = type.GetMethod("SetAxisX");
            _setAxisYMethod = type.GetMethod("SetAxisY");
            _setAxisZMethod = type.GetMethod("SetAxisZ");
            _setRxMethod = type.GetMethod("SetAxisRx"); // Note naming convention check
            _setRyMethod = type.GetMethod("SetAxisRy");
            _setRzMethod = type.GetMethod("SetAxisRz");
            _setSlider0Method = type.GetMethod("SetSlider0");
            _setSlider1Method = type.GetMethod("SetSlider1");
            _setDialMethod = type.GetMethod("SetAxisDial");
            if (_setDialMethod == null) _setDialMethod = type.GetMethod("SetDial");
        }

        private void Reset()
        {
             if (_joyController == null) return;
             var resetMethod = _joyController.GetType().GetMethod("Reset");
             resetMethod?.Invoke(_joyController, null);
        }

        public void Shutdown()
        {
            if (_manager != null && _joyController != null && _relinquishMethod != null)
            {
                try
                {
                    _relinquishMethod.Invoke(_manager, new object[] { _joyController });
                    Console.WriteLine($"[VJoyService] Device {DeviceId} Relinquished.");
                }
                catch { /* Ignore shutdown errors */ }
            }
            IsAvailable = false;
            _joyController = null;
        }

        public void SetAxisX(int value) => InvokeAxis(_setAxisXMethod, value);
        public void SetAxisY(int value) => InvokeAxis(_setAxisYMethod, value);
        public void SetAxisZ(int value) => InvokeAxis(_setAxisZMethod, value);
        public void SetRx(int value) => InvokeAxis(_setRxMethod, value);
        public void SetRy(int value) => InvokeAxis(_setRyMethod, value);
        public void SetRz(int value) => InvokeAxis(_setRzMethod, value);
        public void SetSlider(int value, int index = 0)
        {
            var method = index == 0 ? _setSlider0Method : _setSlider1Method;
            InvokeAxis(method, value);
            
            // Auto-duplicate to Dial for maximum compatibility if it's the main slider
            if (index == 0) SetDial(value);
        }

        public void SetDial(int value) => InvokeAxis(_setDialMethod, value);
        public void SetButtons(uint buttons) { /* Legacy 32-bit support if needed */ }

        public void SetButton(int buttonId, bool state)
        {
            if (!IsAvailable || _joyController == null) return;
            
            try
            {
                // Based on reflection debug: PressButton(UInt32) and ReleaseButton(UInt32)
                string methodName = state ? "PressButton" : "ReleaseButton";
                var method = _joyController.GetType().GetMethod(methodName, new[] { typeof(uint) });
                method?.Invoke(_joyController, new object[] { (uint)buttonId });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VJoyService] SetButton failed: {ex.Message}");
            }
        }

        private void InvokeAxis(MethodInfo? method, int value)
        {
            if (IsAvailable && _joyController != null && method != null)
            {
                try
                {
                    int clampedValue = Math.Clamp(value, 1, 32768);
                    method.Invoke(_joyController, new object[] { clampedValue });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[vJoy Error] {method?.Name} failed: {ex.Message}");
                }
            }
        }
    }
}
