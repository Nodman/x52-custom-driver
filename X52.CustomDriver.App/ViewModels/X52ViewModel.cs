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

        public string NubDisplay => $"X {_nubMouse.RawX,3}   Y {_nubMouse.RawY,3}";

        public void ShutdownNubMouse() => _nubMouse.Dispose();

        public X52Profile CurrentProfile
        {
            get => _currentProfile;
            set
            {
                _currentProfile = value;
                WatchMouse(EnsureMouseSettings(value));
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProfileName));
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

        private readonly string[] _buttonNames = { 
            "Trigger", "ButtonFire", "ButtonA", "ButtonB", "ButtonC", "Pinkie", "ButtonD", "ButtonE", 
            "T1", "T2", "T3", "T4", "T5", "T6", "TriggerStage2",
            "Hat1Up", "Hat1Right", "Hat1Down", "Hat1Left",
            "HatRearUp", "HatRearRight", "HatRearDown", "HatRearLeft",
            "MfdFunction", "MfdStartStop", "MfdReset", "ClutchButton", "MouseLeftClick",
            "Hat2Up", "Hat2Down", "Hat2Left", "Hat2Right"
        };

        private void InitializeButtons()
        {
            for (int i = 0; i < 32; i++)
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

        private void ProcessKeyMappings()
        {
            foreach (var mapping in CurrentProfile.Mappings)
            {
                // Check Mode (0 = All modes, else must match CurrentMode)
                if (mapping.Mode != 0 && mapping.Mode != State.CurrentMode)
                    continue;

                bool currentState = GetButtonState(State, mapping.ButtonName);
                bool prevState = GetButtonState(_prevState, mapping.ButtonName);

                if (currentState && !prevState) // On Press
                {
                    if (mapping.KeySequence != null)
                        _keyboardService.SendKeys(mapping.KeySequence);
                }
            }
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
            
            // --- GHOSTBUSTER LOGIC (Synced & Fixed Botón 24) ---
            if (s.RawData != null)
            {
                 int vBtn = 1 + (s.CurrentMode - 1) * 32;
                 
                 for (int b = 8; b < Math.Min(s.RawData.Length, 12); b++)
                 {
                     for (int bit = 0; bit < 8; bit++)
                     {
                         // GHOSTBUSTER: Ignore internal Mode bits
                         // B10 Bit 7 = Mode 1 (Standard)
                         // B11 Bits 0 & 1 = Mode 2 & 3 (Standard)
                         if ((b == 10 && bit == 7) || (b == 11 && (bit == 0 || bit == 1)))
                         {
                             vBtn++;
                             continue;
                         }

                         if (vBtn <= 128)
                             _vJoyService.SetButton(vBtn++, (s.RawData[b] & (1 << bit)) != 0);
                     }
                 }

                 // SILVER BULLET PHASE 2: Explicit Clean Mapping for Hat 2
                 int baseId = 1 + (s.CurrentMode - 1) * 32;
                 _vJoyService.SetButton(baseId + 28, s.Hat2Up);    // Button 29
                 _vJoyService.SetButton(baseId + 29, s.Hat2Down);  // Button 30
                 _vJoyService.SetButton(baseId + 30, s.Hat2Left);  // Button 31
                 _vJoyService.SetButton(baseId + 31, s.Hat2Right); // Button 32
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
