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

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Thumb stick ("mouse nub") behaviour for one profile.</summary>
    public class ThumbMouseSettings : INotifyPropertyChanged
    {
        private bool _enabled = true;
        private bool _buttons = true;
        private double _speed = 1200;
        private double _deadzone = 1.0;

        public bool Enabled { get => _enabled; set { _enabled = value; OnPropertyChanged(nameof(Enabled)); } }
        public bool Buttons { get => _buttons; set { _buttons = value; OnPropertyChanged(nameof(Buttons)); } }
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
        public bool IsToggle { get; set; } = false;
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

        // Response curves sent to vJoy (stick X = roll, stick Y = pitch, twist = rudder)
        public AxisCurve CurveX { get; set; } = new();
        public AxisCurve CurveY { get; set; } = new();
        public AxisCurve CurveTwist { get; set; } = new();
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
