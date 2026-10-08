using System;

namespace X52.CustomDriver.Core.Models
{
    public class AppSettings
    {
        public bool MinimizeToTray { get; set; } = true;
        public bool CloseToTray { get; set; } = true;
        public bool RunAtStartup { get; set; } = false;
        public bool UpgradeRequired { get; set; } = true;

        // Throttle thumb stick ("mouse nub") -> Windows mouse
        public bool NubMouseEnabled { get; set; } = true;
        public bool NubMouseButtons { get; set; } = true;
        public double NubMouseSpeed { get; set; } = 1200;
        public double NubMouseDeadzone { get; set; } = 1.0;
        public int NubMouseRotation { get; set; } = 0;   // degrees clockwise: 0, 90, 180, 270
        public bool NubMouseInvertX { get; set; } = false;
        public bool NubMouseInvertY { get; set; } = false;
    }
}
