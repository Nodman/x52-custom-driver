using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Generic;

namespace X52.CustomDriver.Core.Models
{
    public class X52Profile : INotifyPropertyChanged
    {
        private string _name = "Default";
        private string? _processName;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        public string? ProcessName // e.g. "DCS", "FlightSimulator" (with or without .exe)
        {
            get => _processName;
            set { _processName = value; OnPropertyChanged(nameof(ProcessName)); }
        }

        public ObservableCollection<ButtonMapping> Mappings { get; set; } = new();
        public AxisSettings AxisSettings { get; set; } = new();

        // Null in profiles saved by older versions; filled in on load
        public ThumbMouseSettings? Mouse { get; set; }

        // Edited since the last save (edits apply right away, but are only written on SAVE).
        // Kept up to date by ProfileService.RefreshUnsavedFlags.
        private bool _hasUnsavedChanges;
        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set { if (_hasUnsavedChanges != value) { _hasUnsavedChanges = value; OnPropertyChanged(nameof(HasUnsavedChanges)); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Thumb stick ("mouse nub") behaviour for one profile.</summary>
    public class ThumbMouseSettings : INotifyPropertyChanged
    {
        private bool _enabled = true;
        private bool _moveCursor = true;
        private bool _leftClick = true;
        private bool _middleClick = true;
        private bool _scroll = true;
        private double _speed = 1200;
        private double _deadzone = 1.0;

        // Master switch (Ctrl+Alt+M)
        public bool Enabled { get => _enabled; set { _enabled = value; OnPropertyChanged(nameof(Enabled)); } }
        public bool MoveCursor { get => _moveCursor; set { _moveCursor = value; OnPropertyChanged(nameof(MoveCursor)); } }
        public bool LeftClick { get => _leftClick; set { _leftClick = value; OnPropertyChanged(nameof(LeftClick)); } }     // throttle mouse button
        public bool MiddleClick { get => _middleClick; set { _middleClick = value; OnPropertyChanged(nameof(MiddleClick)); } } // wheel press
        public bool Scroll { get => _scroll; set { _scroll = value; OnPropertyChanged(nameof(Scroll)); } }               // wheel up/down

        // Old single "buttons" switch (v1.2.x). Only read from old files; never written.
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public bool? Buttons
        {
            get => null;
            set { if (value.HasValue) { LeftClick = value.Value; MiddleClick = value.Value; Scroll = value.Value; } }
        }
        public double Speed { get => _speed; set { _speed = value; OnPropertyChanged(nameof(Speed)); } }       // px/s at full deflection
        public double Deadzone { get => _deadzone; set { _deadzone = value; OnPropertyChanged(nameof(Deadzone)); } } // 0..4 nub units

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ButtonMapping
    {
        public string ButtonName { get; set; } = ""; // e.g. "Trigger", "ButtonD"
        public bool EnableVJoy { get; set; } = true;
        public List<string>? KeySequence { get; set; } // e.g. ["LSHIFT", "G"]
        public bool IsToggle { get; set; } = false; // legacy (v1.1.8); only used to migrate old mappings

        // "Hold":   keys held while the button is held (no auto-repeat; games read the key state)
        // "Repeat": like Hold, plus keyboard-style auto-repeat (for menus / text fields)
        // "Tap":    one short press when the button goes down
        // "Toggle": first press holds the keys, next press releases them
        // Null in mappings saved before v1.2.2: those were one-shot presses, so they count as Tap
        // (or Toggle if the old IsToggle flag was set). See EffectiveAction / ProfileService load.
        public string? Action { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public string EffectiveAction => string.IsNullOrWhiteSpace(Action) ? (IsToggle ? "Toggle" : "Tap") : Action!;
        public int Mode { get; set; } = 0; // 0 = All, 1, 2, 3

        [System.Text.Json.Serialization.JsonIgnore]
        public string KeySequenceString
        {
            get => KeySequence != null ? string.Join("+", KeySequence) : "";
            set
            {
                if (string.IsNullOrWhiteSpace(value)) KeySequence = null;
                else KeySequence = new List<string>(value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
        }
    }

    public class AxisSettings
    {
        public double SensitivityX { get; set; } = 1.0;
        public double SensitivityY { get; set; } = 1.0;
        public double DeadzoneZ { get; set; } = 40;
        public bool InvertThrottle { get; set; } = false;

        // Throttle deadzones (lower / upper end, optional centre)
        public ThrottleSettings Throttle { get; set; } = new();

        // Response curves sent to vJoy (stick X = roll, stick Y = pitch, twist = rudder)
        public AxisCurve CurveX { get; set; } = new();
        public AxisCurve CurveY { get; set; } = new();
        public AxisCurve CurveTwist { get; set; } = new();
    }

    /// <summary>
    /// Throttle response: deadzones at both ends and an optional one around a centre point.
    /// Input and output are throttle positions 0..1 (0 = pulled back / idle, 1 = full forward).
    /// All values are fractions of the travel.
    /// </summary>
    public class ThrottleSettings
    {
        public double LowerDeadzone { get; set; } = 0.0;   // travel at the idle end that still counts as 0%
        public double UpperDeadzone { get; set; } = 0.0;   // travel at the full end that already counts as 100%
        public double CenterDeadzone { get; set; } = 0.0;  // total width of the band that outputs exactly 50% (0 = off)
        public double CenterPosition { get; set; } = 0.5;  // where that band sits (physical position)
        public bool Invert { get; set; } = false;

        public const double MaxEndDeadzone = 0.4, MaxCenterDeadzone = 0.4;

        /// <summary>The zones as physical positions, for drawing: active travel lo..hi, centre band.</summary>
        public (double lo, double hi, bool hasCenter, double centerLo, double centerHi) Zones()
        {
            double lo = Math.Clamp(LowerDeadzone, 0.0, MaxEndDeadzone);
            double hi = 1.0 - Math.Clamp(UpperDeadzone, 0.0, MaxEndDeadzone);
            if (hi < lo + 0.05) hi = Math.Min(1.0, lo + 0.05);

            double half = Math.Clamp(CenterDeadzone, 0.0, MaxCenterDeadzone) / 2.0;
            if (half <= 0.0) return (lo, hi, false, 0, 0);

            // Keep the band inside the active travel with a little room on both sides
            double min = lo + half + 0.01, max = hi - half - 0.01;
            if (max < min) return (lo, hi, false, 0, 0);
            double c = Math.Clamp(CenterPosition, min, max);
            return (lo, hi, true, c - half, c + half);
        }

        public double Apply(double t)
        {
            t = Math.Clamp(t, 0.0, 1.0);
            var (lo, hi, hasCenter, cLo, cHi) = Zones();
            double u;
            if (!hasCenter) u = Lin(t, lo, hi);
            else if (t <= cLo) u = 0.5 * Lin(t, lo, cLo);
            else if (t >= cHi) u = 0.5 + 0.5 * Lin(t, cHi, hi);
            else u = 0.5;
            return Invert ? 1.0 - u : u;
        }

        private static double Lin(double t, double a, double b) =>
            b <= a ? (t >= b ? 1.0 : 0.0) : Math.Clamp((t - a) / (b - a), 0.0, 1.0);

        public void Reset()
        {
            LowerDeadzone = 0.0; UpperDeadzone = 0.0; CenterDeadzone = 0.0; CenterPosition = 0.5; Invert = false;
        }
    }

    /// <summary>
    /// DCS-style axis response curve. All values are fractions (0..1).
    /// Input and output are centred axes in the range -1..1.
    /// </summary>
    public class AxisCurve
    {
        public double Deadzone { get; set; } = 0.0;     // ignore this much travel around centre
        public double Curvature { get; set; } = 0.0;    // 0 = linear, 1 = full cubic (soft centre)
        public double SaturationX { get; set; } = 1.0;  // input travel at which output reaches its max
        public double SaturationY { get; set; } = 1.0;  // maximum output
        public bool Invert { get; set; } = false;

        public double Apply(double t)
        {
            t = Math.Clamp(t, -1.0, 1.0);
            double a = Math.Abs(t);
            double dz = Math.Clamp(Deadzone, 0.0, 0.5);
            double satX = Math.Clamp(SaturationX, dz + 0.01, 1.0);
            double satY = Math.Clamp(SaturationY, 0.0, 1.0);
            double k = Math.Clamp(Curvature, 0.0, 1.0);

            double u = a <= dz ? 0.0 : Math.Min((a - dz) / (satX - dz), 1.0);
            double y = ((1.0 - k) * u + k * u * u * u) * satY;
            double o = Math.Sign(t) * y;
            return Invert ? -o : o;
        }

        public void Reset()
        {
            Deadzone = 0.0; Curvature = 0.0; SaturationX = 1.0; SaturationY = 1.0; Invert = false;
        }
    }
}
