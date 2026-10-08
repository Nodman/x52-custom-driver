using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;

namespace X52.CustomDriver.Core.Models
{
    public class X52Profile
    {
        public string Name { get; set; } = "Default";
        public string? ProcessName { get; set; } // e.g. "DCS", "FlightSimulator"
        public ObservableCollection<ButtonMapping> Mappings { get; set; } = new();
        public AxisSettings AxisSettings { get; set; } = new();
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
