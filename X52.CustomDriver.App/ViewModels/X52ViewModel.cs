using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using X52.CustomDriver.Core.Interfaces;
using X52.CustomDriver.Core.Models;
using X52.CustomDriver.Core.Services;

namespace X52.CustomDriver.App.ViewModels
{
    public class X52ViewModel : INotifyPropertyChanged
    {
        private readonly IHidService _hidService;
        private readonly IVJoyService _vJoyService;
        private readonly KeyboardService _keyboardService = new();
        private readonly NubMouseService _nubMouse = new();
        public NubMouseService NubMouse => _nubMouse;
        private readonly ProfileService _profileService;
        private readonly SettingsService _settingsService;
        public ProfileService ProfileService => _profileService;
        public SettingsService SettingsService => _settingsService;

        private X52State _state = new();
        private X52State _prevState = new();
        private X52Profile _currentProfile = new();

        public X52State State
        {
            get => _state;
            set { _prevState = _state; _state = value; OnPropertyChanged(); }
        }
        
        // Settings Properties
        public bool MinimizeToTray
        {
            get => _settingsService.CurrentSettings.MinimizeToTray;
            set 
            { 
                _settingsService.CurrentSettings.MinimizeToTray = value; 
                _settingsService.SaveSettings(); 
                OnPropertyChanged(); 
            }
        }

        public bool CloseToTray
        {
            get => _settingsService.CurrentSettings.CloseToTray;
            set 
            { 
                _settingsService.CurrentSettings.CloseToTray = value; 
                _settingsService.SaveSettings(); 
                OnPropertyChanged(); 
            }
        }

        public bool RunAtStartup
        {
            get => _settingsService.CurrentSettings.RunAtStartup;
            set 
            { 
                _settingsService.CurrentSettings.RunAtStartup = value; 
                _settingsService.SaveSettings(); 
                OnPropertyChanged(); 
            }
        }

        // --- Thumb stick (mouse nub) -> Windows mouse ---
        // Enable/buttons/speed/deadzone live in each profile (see CurrentProfile / ThumbMouseSettings).
        // Orientation is a property of the hardware and is shared by all profiles.

        public int NubMouseRotation
        {
            get => _settingsService.CurrentSettings.NubMouseRotation;
            set
            {
                int r = ((value % 360) + 360) % 360 / 90 * 90;
                _settingsService.CurrentSettings.NubMouseRotation = r;
                _nubMouse.Rotation = r;
                _settingsService.SaveSettings();
                OnPropertyChanged();
                OnPropertyChanged(nameof(NubRotationText));
            }
        }

        public string NubRotationText => NubMouseRotation switch
        {
            90 => "90° clockwise",
            180 => "180°",
            270 => "90° counter-clockwise",
            _ => "0° (none)"
        };

        public void RotateNubMouse() => NubMouseRotation += 90;

        public bool NubMouseInvertX
        {
            get => _settingsService.CurrentSettings.NubMouseInvertX;
            set
            {
                _settingsService.CurrentSettings.NubMouseInvertX = value;
                _nubMouse.InvertX = value;
                _settingsService.SaveSettings();
                OnPropertyChanged();
            }
        }

        public bool NubMouseInvertY
        {
            get => _settingsService.CurrentSettings.NubMouseInvertY;
            set
            {
                _settingsService.CurrentSettings.NubMouseInvertY = value;
                _nubMouse.InvertY = value;
                _settingsService.SaveSettings();
                OnPropertyChanged();
            }
        }

        // --- Hide the real X52 from games (HidHide) ---
        public bool HidHideInstalled { get; private set; }
        public bool HideRealX52Enabled => _settingsService.CurrentSettings.HideRealX52;
        public string HideRealX52Status { get; private set; } = "";
        public bool HideRealX52Busy { get; private set; }

        /// <summary>Re-check HidHide and whether the stick on its current USB port is hidden.</summary>
        public void RefreshHidHideStatus()
        {
            HidHideInstalled = HidHideManager.IsInstalled;
            var cfg = _settingsService.CurrentSettings;
            string? id = _hidService.DeviceInstanceId;

            if (!HidHideInstalled)
                HideRealX52Status = "HidHide is not installed. Install it, restart Windows, then come back here.";
            else if (!cfg.HideRealX52)
                HideRealX52Status = "Games can see the real X52 and the virtual stick (inputs arrive twice).";
            else if (id == null)
                HideRealX52Status = "On. Plug in the X52 to check it.";
            else
            {
                bool? hidden = HidHideManager.IsHidden(id);
                bool known = cfg.HiddenInstanceIds.Any(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
                if (hidden == true || (hidden == null && known))
                    HideRealX52Status = "✓ Hidden. Games only see the Ærakon virtual stick. (Restart games that were already running.)";
                else
                    HideRealX52Status = "⚠ The X52 is on a USB port that isn't hidden yet. Click HIDE again.";
            }

            OnPropertyChanged(nameof(HidHideInstalled));
            OnPropertyChanged(nameof(HideRealX52Enabled));
            OnPropertyChanged(nameof(HideRealX52Status));
        }

        public async Task<string> HideRealX52Async()
        {
            string? id = _hidService.DeviceInstanceId;
            if (id == null) return "Plug in the X52 first.";

            var cfg = _settingsService.CurrentSettings;
            var ids = cfg.HiddenInstanceIds.Append(id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            SetBusy(true);
            var (ok, message) = await HidHideManager.HideAsync(ids);
            SetBusy(false);

            if (ok)
            {
                cfg.HideRealX52 = true;
                cfg.HiddenInstanceIds = ids;
                _settingsService.SaveSettings();
            }
            RefreshHidHideStatus();
            return ok ? "" : message;
        }

        public async Task<string> ShowRealX52Async()
        {
            var cfg = _settingsService.CurrentSettings;
            var ids = cfg.HiddenInstanceIds.ToList();
            string? id = _hidService.DeviceInstanceId;
            if (id != null && !ids.Contains(id, StringComparer.OrdinalIgnoreCase)) ids.Add(id);

            SetBusy(true);
            var (ok, message) = ids.Count == 0 ? (true, "") : await HidHideManager.ShowAsync(ids);
            SetBusy(false);

            if (ok)
            {
                cfg.HideRealX52 = false;
                cfg.HiddenInstanceIds.Clear();
                _settingsService.SaveSettings();
            }
            RefreshHidHideStatus();
            return ok ? "" : message;
        }

        private void SetBusy(bool busy)
        {
            HideRealX52Busy = busy;
            OnPropertyChanged(nameof(HideRealX52Busy));
        }

        public string NubDisplay => $"X {_nubMouse.RawX,3}   Y {_nubMouse.RawY,3}";

        public void ShutdownNubMouse()
        {
            _nubMouse.Dispose();
            ReleaseAllMappedKeys();
        }

        public X52Profile CurrentProfile
        {
            get => _currentProfile;
            set
            {
                if (!ReferenceEquals(_currentProfile, value)) ReleaseAllMappedKeys();
                _currentProfile = value;
                WatchMouse(EnsureMouseSettings(value));
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProfileName));
                OnPropertyChanged(nameof(ActiveProfileSelection));
                OnPropertyChanged(nameof(NubMouseStatus));
            }
        }

        /// <summary>
        /// Profiles saved before v1.2.0 have no thumb stick settings: give them the
        /// (then global) values from settings.json so nothing changes for the user.
        /// </summary>
        public ThumbMouseSettings EnsureMouseSettings(X52Profile profile)
        {
            if (profile.Mouse == null)
            {
                var cfg = _settingsService.CurrentSettings;
                profile.Mouse = new ThumbMouseSettings
                {
                    Enabled = cfg.NubMouseEnabled,
                    Buttons = cfg.NubMouseButtons,
                    Speed = cfg.NubMouseSpeed,
                    Deadzone = cfg.NubMouseDeadzone
                };
            }
            return profile.Mouse;
        }

        private ThumbMouseSettings? _watchedMouse;

        // The mouse service always follows the active profile; refresh the LIVE status when it changes
        private void WatchMouse(ThumbMouseSettings settings)
        {
            if (_watchedMouse != null) _watchedMouse.PropertyChanged -= WatchedMouse_PropertyChanged;
            _watchedMouse = settings;
            _watchedMouse.PropertyChanged += WatchedMouse_PropertyChanged;
            _nubMouse.Settings = settings;
        }

        private void WatchedMouse_PropertyChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(nameof(NubMouseStatus));

        /// <summary>Ctrl+Alt+M: switch the thumb stick mouse of the active profile on/off.</summary>
        public void ToggleNubMouse()
        {
            var m = EnsureMouseSettings(CurrentProfile);
            m.Enabled = !m.Enabled;
            _profileService.SaveProfiles();
            OnPropertyChanged(nameof(NubMouseStatus));
        }

        public string NubMouseStatus => EnsureMouseSettings(CurrentProfile).Enabled ? "ON" : "OFF";

        public string ProfileName => CurrentProfile.Name;

        // --- Manual profile switching ---
        public IReadOnlyList<X52Profile> Profiles => _profileService.Profiles;

        public void ActivateProfile(X52Profile profile) => _profileService.SetActiveProfile(profile);

        /// <summary>Two-way target for the profile picker on the LIVE tab.</summary>
        public X52Profile? ActiveProfileSelection
        {
            get => CurrentProfile;
            set { if (value != null) ActivateProfile(value); }
        }

        public string RawDataString => State.RawData != null ? BitConverter.ToString(State.RawData).Replace("-", " ") : "No Data";

        public double XPercent => (State.X / 2048.0) * 100;
        public double YPercent => (State.Y / 2048.0) * 100;
        public double ZPercent => (State.Z / 1024.0) * 100;
        public double ThrottlePercent => (State.Throttle / 255.0) * 100;
        public double Rotary1Percent => (State.Rotary1 / 255.0) * 100;
        public double Rotary2Percent => (State.Rotary2 / 255.0) * 100;
        public double SliderPercent => (State.Slider / 255.0) * 100;

        public bool IsConnected => _hidService.IsConnected;
        public bool IsVJoyActive => _vJoyService.IsAvailable;
        public string VJoyDeviceName => _vJoyService.DeviceName;

        public X52ViewModel(IHidService hidService, IVJoyService vJoyService, ProfileService profileService, SettingsService settingsService)
        {
            _hidService = hidService;
            _vJoyService = vJoyService;
            _profileService = profileService;
            _settingsService = settingsService;

            InitializeButtons();

            // Work on the real active profile (not a throwaway copy), so edits are saved
            _currentProfile = _profileService.ActiveProfile;

            // Migrate pre-1.2.0 global thumb stick settings into every profile
            bool migrated = false;
            foreach (var p in _profileService.Profiles)
                if (p.Mouse == null) { EnsureMouseSettings(p); migrated = true; }
            if (migrated) _profileService.SaveProfiles();
            WatchMouse(EnsureMouseSettings(_currentProfile));

            var cfg = _settingsService.CurrentSettings;
            _nubMouse.Rotation = cfg.NubMouseRotation;
            _nubMouse.InvertX = cfg.NubMouseInvertX;
            _nubMouse.InvertY = cfg.NubMouseInvertY;

            _profileService.OnProfileChanged += (s, p) =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    CurrentProfile = p;
                });
            };

            _hidService.OnDisconnected += (s, e) =>
            {
                // Stop the cursor immediately and leave the game with a centred, idle stick
                _nubMouse.Reset();
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(ReleaseAllMappedKeys));
                var last = _state;
                UpdateVJoy(new X52State
                {
                    X = 1024, Y = 1024, Z = 512,
                    Throttle = last.Throttle, Rotary1 = last.Rotary1, Rotary2 = last.Rotary2, Slider = last.Slider,
                    CurrentMode = last.CurrentMode,
                    RawData = new byte[Math.Max(last.RawData?.Length ?? 0, 14)]
                });
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => OnPropertyChanged(nameof(IsConnected))));
            };

            _hidService.OnStateChanged += (s, e) =>
            {
                // Mouse emulation runs on the HID thread so it never waits for the UI
                _nubMouse.Update(e.RawData, e.ProductId);

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    State = e;
                    UpdatePhysicalButtons(e);
                    ProcessKeyMappings();
                    
                    OnPropertyChanged(nameof(XPercent));
                    OnPropertyChanged(nameof(YPercent));
                    OnPropertyChanged(nameof(ZPercent));
                    OnPropertyChanged(nameof(ThrottlePercent));
                    OnPropertyChanged(nameof(Rotary1Percent));
                    OnPropertyChanged(nameof(Rotary2Percent));
                    OnPropertyChanged(nameof(SliderPercent));
                    OnPropertyChanged(nameof(IsConnected));
                    OnPropertyChanged(nameof(IsVJoyActive));
                    OnPropertyChanged(nameof(RawDataString));
                    OnPropertyChanged(nameof(NubDisplay));
                    OnPropertyChanged(nameof(HeldKeysText));
                    OnPropertyChanged(nameof(IsMode1));
                    OnPropertyChanged(nameof(IsMode2));
                    OnPropertyChanged(nameof(IsMode3));
                });

                UpdateVJoy(e);
            };
        }

        public bool IsMode1 => State.CurrentMode == 1;
        public bool IsMode2 => State.CurrentMode == 2;
        public bool IsMode3 => State.CurrentMode == 3;

        public System.Collections.ObjectModel.ObservableCollection<ButtonVisualState> PhysicalButtons { get; } = new();

        private static string[] _buttonNames => VJoyButtonLayout;

        private void InitializeButtons()
        {
            for (int i = 0; i < VJoyButtonLayout.Length; i++)
            {
                PhysicalButtons.Add(new ButtonVisualState { Name = (i + 1).ToString() });
            }
        }

        private void UpdatePhysicalButtons(X52State s)
        {
            for (int i = 0; i < _buttonNames.Length && i < PhysicalButtons.Count; i++)
            {
                PhysicalButtons[i].IsPressed = GetButtonState(s, _buttonNames[i]);
            }
        }

        // Mappings whose keys are currently held down, with the exact keys that were pressed
        private readonly Dictionary<ButtonMapping, List<string>> _heldMappings = new();

        private void ProcessKeyMappings()
        {
            var mappings = CurrentProfile.Mappings;

            // 1) Release: Hold mappings whose button was let go, and anything no longer in the profile
            foreach (var held in _heldMappings.ToList())
            {
                var m = held.Key;
                bool stillMapped = mappings.Contains(m);
                bool isToggle = string.Equals(m.Action, "Toggle", StringComparison.OrdinalIgnoreCase);
                bool buttonDown = GetButtonState(State, m.ButtonName);
                if (!stillMapped || (!isToggle && !buttonDown))
                {
                    _keyboardService.Release(held.Value);
                    _heldMappings.Remove(m);
                }
            }

            // 2) Press: react to buttons that just went down
            foreach (var mapping in mappings)
            {
                if (mapping.KeySequence == null || mapping.KeySequence.Count == 0) continue;

                // Mode 0 = all modes, else must match the mode dial
                if (mapping.Mode != 0 && mapping.Mode != State.CurrentMode) continue;

                bool currentState = GetButtonState(State, mapping.ButtonName);
                bool prevState = GetButtonState(_prevState, mapping.ButtonName);
                if (!currentState || prevState) continue; // only on the press edge

                switch ((mapping.Action ?? "Hold").ToLowerInvariant())
                {
                    case "tap":
                        _keyboardService.Tap(mapping.KeySequence);
                        break;

                    case "toggle":
                        if (_heldMappings.TryGetValue(mapping, out var keys))
                        {
                            _keyboardService.Release(keys);
                            _heldMappings.Remove(mapping);
                        }
                        else
                        {
                            var copy = mapping.KeySequence.ToList();
                            _keyboardService.Press(copy);
                            _heldMappings[mapping] = copy;
                        }
                        break;

                    default: // hold
                        if (!_heldMappings.ContainsKey(mapping))
                        {
                            var copy = mapping.KeySequence.ToList();
                            _keyboardService.Press(copy);
                            _heldMappings[mapping] = copy;
                        }
                        break;
                }
            }
        }

        /// <summary>Let go of every key held by a mapping (profile switch, unplug, exit).</summary>
        public string HeldKeysText
        {
            get
            {
                string keys = _keyboardService.HeldKeysText;
                return keys.Length == 0 ? "none" : keys;
            }
        }

        public void ReleaseAllMappedKeys()
        {
            _heldMappings.Clear();
            _keyboardService.ReleaseAll();
        }

        private bool GetButtonState(X52State state, string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var prop = typeof(X52State).GetProperty(name);
            return (bool)(prop?.GetValue(state) ?? false);
        }

        private void UpdateVJoy(X52State s)
        {
            if (!_vJoyService.IsAvailable) return;
            
            // --- AXES MAPPING (Synced with Console v1.1.7) ---
            // Axis response curves (per profile)
            var axes = CurrentProfile.AxisSettings;
            int vX = CurveToVJoy(axes.CurveX, s.X, 2048);
            int vY = CurveToVJoy(axes.CurveY, s.Y, 2048);
            int vZ = CurveToVJoy(axes.CurveTwist, s.Z, 1024);

            // Throttle Calibration: Physical [245 -> 10] maps to [255 -> 0]
            // Scale to vJoy (0-32768)
            int vT = (255 - s.Throttle) * 128; 
            
            int vR1 = s.Rotary1 * 128;
            int vR2 = s.Rotary2 * 128;
            int vS = s.Slider * 128;

            _vJoyService.SetAxisX(vX);
            _vJoyService.SetAxisY(vY);
            _vJoyService.SetAxisZ(vZ);
            _vJoyService.SetRx(vT);
            _vJoyService.SetRy(vR1);
            _vJoyService.SetRz(vR2);
            _vJoyService.SetSlider(vS);
            
            // --- Buttons and POV hat ---
            UpdateVJoyButtons(s);
            _vJoyService.SetPov(HatDirection(s));
        }

        /// <summary>
        /// vJoy button numbers (1-based, per mode bank). Built from the decoded button states,
        /// so every physical button has a fixed number. Buttons 33-64 / 65-96 repeat this list
        /// for modes 2 / 3 when "mode dial shifts buttons" is on.
        /// </summary>
        public static readonly string[] VJoyButtonLayout =
        {
            "Trigger", "ButtonFire", "ButtonA", "ButtonB", "ButtonC", "Pinkie", "ButtonD", "ButtonE",
            "T1", "T2", "T3", "T4", "T5", "T6", "TriggerStage2",
            "Hat1Up", "Hat1Right", "Hat1Down", "Hat1Left",
            "HatRearUp", "HatRearRight", "HatRearDown", "HatRearLeft",
            "ClutchButton", "MfdFunction", "MfdStartStop", "MfdReset",
            "MouseLeftClick", "MouseWheelClick", "MouseWheelDown", "MouseWheelUp"
        };
        private const int ButtonsPerBank = 32;

        private readonly bool[] _vjoyPressed = new bool[129];

        private void SetVJoyButton(int number, bool pressed)
        {
            if (number < 1 || number > 128 || number > Math.Max(_vJoyService.ButtonCount, 1)) return;
            if (_vjoyPressed[number] == pressed) return; // only send changes
            _vjoyPressed[number] = pressed;
            _vJoyService.SetButton(number, pressed);
        }

        private void UpdateVJoyButtons(X52State s)
        {
            bool banks = _settingsService.CurrentSettings.ModeShiftsButtons;
            int bank = banks ? Math.Clamp(s.CurrentMode, 1, 3) - 1 : 0;

            // Buttons whose key mapping has the "vJoy" box unticked are not sent to vJoy
            var suppressed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in CurrentProfile.Mappings)
                if (!m.EnableVJoy && (m.Mode == 0 || m.Mode == s.CurrentMode)) suppressed.Add(m.ButtonName);

            for (int b = 0; b < 3; b++)
            {
                for (int i = 0; i < VJoyButtonLayout.Length; i++)
                {
                    int number = b * ButtonsPerBank + i + 1;
                    bool pressed = b == bank && !suppressed.Contains(VJoyButtonLayout[i]) && GetButtonState(s, VJoyButtonLayout[i]);
                    SetVJoyButton(number, pressed);
                }
            }
        }

        /// <summary>Stick-top hat (8-way) as degrees clockwise from up, or -1 when centred.</summary>
        private static int HatDirection(X52State s)
        {
            bool u = s.Hat2Up, r = s.Hat2Right, d = s.Hat2Down, l = s.Hat2Left;
            if (u && r) return 45;
            if (d && r) return 135;
            if (d && l) return 225;
            if (u && l) return 315;
            if (u) return 0;
            if (r) return 90;
            if (d) return 180;
            if (l) return 270;
            return -1;
        }

        public string VJoyInfoText
        {
            get
            {
                if (!_vJoyService.IsAvailable) return "vJoy device #1 is not available. Install vJoy and enable device 1 in Configure vJoy.";
                int buttons = _vJoyService.ButtonCount;
                int cont = _vJoyService.ContinuousPovCount, disc = _vJoyService.DiscretePovCount;
                string pov = cont > 0 ? $"{cont} continuous POV hat(s)" : disc > 0 ? $"{disc} 4-way POV hat(s)" : "no POV hat";
                int needed = _settingsService.CurrentSettings.ModeShiftsButtons ? 3 * ButtonsPerBank : VJoyButtonLayout.Length;
                string advice = buttons < needed || (cont == 0 && disc == 0)
                    ? "  → Click SET UP VJOY below to fix this automatically."
                    : "  ✓ Enough for every X52 button" + (cont > 0 ? " and the 8-way hat." : ".");
                return $"vJoy device #1: {buttons} buttons, {pov}.{advice}";
            }
        }

        public bool VJoyNeedsSetup
        {
            get
            {
                if (!_vJoyService.IsAvailable) return true;
                int needed = _settingsService.CurrentSettings.ModeShiftsButtons ? 3 * ButtonsPerBank : VJoyButtonLayout.Length;
                return _vJoyService.ButtonCount < needed || (_vJoyService.ContinuousPovCount == 0 && _vJoyService.DiscretePovCount == 0);
            }
        }

        /// <summary>
        /// Reconfigure vJoy device 1 for the X52 with vJoy's own command-line tool:
        /// all 8 axes, 128 buttons, 1 continuous POV. Needs admin rights (one UAC prompt).
        /// </summary>
        public async Task<string> SetUpVJoyAsync()
        {
            string? tool = FindVJoyTool("vJoyConfig.exe");
            if (tool == null) return "vJoyConfig.exe wasn't found. Is vJoy installed in Program Files\\vJoy?";

            // Let go of the device while vJoy rebuilds it
            for (int n = 1; n <= 128; n++) _vjoyPressed[n] = false;
            _vJoyService.Shutdown();

            string error = await Task.Run(() =>
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(tool, "1 -f -a x y z rx ry rz sl0 sl1 -b 128 -p 1")
                    {
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                    };
                    using var p = System.Diagnostics.Process.Start(psi);
                    if (p == null) return "Could not start vJoyConfig.";
                    if (!p.WaitForExit(60000)) return "vJoyConfig did not finish in time.";
                    return "";
                }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    return "Cancelled – admin permission is needed to change vJoy.";
                }
                catch (Exception ex) { return ex.Message; }
            });

            // vJoy restarts its device; give Windows a moment, then take it again
            for (int attempt = 0; attempt < 10 && !_vJoyService.IsAvailable; attempt++)
            {
                await Task.Delay(1000);
                _vJoyService.Initialize(1);
            }

            OnPropertyChanged(nameof(IsVJoyActive));
            OnPropertyChanged(nameof(VJoyInfoText));
            OnPropertyChanged(nameof(VJoyNeedsSetup));
            if (error.Length > 0) return error;
            if (!_vJoyService.IsAvailable) return "vJoy was changed but the device didn't come back. Restart this driver.";
            return VJoyNeedsSetup ? "vJoyConfig ran but the device still reports too few buttons. Try Configure vJoy." : "";
        }

        public static string? FindVJoyTool(string exeName)
        {
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string[] candidates =
            {
                System.IO.Path.Combine(pf, "vJoy", "x64", exeName),
                System.IO.Path.Combine(pf, "vJoy", exeName),
                System.IO.Path.Combine(pf86, "vJoy", "x64", exeName),
                System.IO.Path.Combine(pf86, "vJoy", exeName)
            };
            return candidates.FirstOrDefault(System.IO.File.Exists);
        }

        public bool ModeShiftsButtons
        {
            get => _settingsService.CurrentSettings.ModeShiftsButtons;
            set
            {
                // Release everything first so nothing stays pressed in the old layout
                for (int n = 1; n <= 128; n++) SetVJoyButton(n, false);
                _settingsService.CurrentSettings.ModeShiftsButtons = value;
                _settingsService.SaveSettings();
                OnPropertyChanged();
                OnPropertyChanged(nameof(VJoyInfoText));
                OnPropertyChanged(nameof(VJoyNeedsSetup));
            }
        }

        // --- Axis curves ---
        private static double Centered(int value, int range) => Math.Clamp((value - range / 2.0) / (range / 2.0), -1.0, 1.0);

        private static int CurveToVJoy(AxisCurve? curve, int value, int range)
        {
            double t = Centered(value, range);
            double o = curve != null ? curve.Apply(t) : t;
            return Math.Clamp((int)Math.Round(16384 + o * 16384), 0, 32768);
        }

        public AxisCurve GetCurve(string axis)
        {
            var a = CurrentProfile.AxisSettings;
            switch (axis)
            {
                case "Y": return a.CurveY ??= new AxisCurve();
                case "Twist": return a.CurveTwist ??= new AxisCurve();
                default: return a.CurveX ??= new AxisCurve();
            }
        }

        /// <summary>Current physical position of an axis, -1..1, before the curve.</summary>
        public double GetLiveInput(string axis) => axis switch
        {
            "Y" => Centered(State.Y, 2048),
            "Twist" => Centered(State.Z, 1024),
            _ => Centered(State.X, 2048)
        };

        public void SaveProfiles() => _profileService.SaveProfiles();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public void CalibrateCenter() => _hidService.CalibrateCenter();
        public void ResetCalibration() => _hidService.ResetCalibration();
    }

    public class ButtonVisualState : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private bool _isPressed;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public bool IsPressed
        {
            get => _isPressed;
            set { _isPressed = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
