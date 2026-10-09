using System;

namespace X52.CustomDriver.Core.Models
{
    public class AppSettings
    {
        public bool MinimizeToTray { get; set; } = true;
        public bool CloseToTray { get; set; } = true;
        public bool RunAtStartup { get; set; } = false;
        public bool UpgradeRequired { get; set; } = true;

        // Real X52 hidden from games with HidHide; device instance IDs we added (one per USB port used)
        // vJoy buttons 1-32 in mode 1, 33-64 in mode 2, 65-96 in mode 3 (like the original Saitek driver)
        public bool ModeShiftsButtons { get; set; } = true;

        public bool HideRealX52 { get; set; } = false;
        public List<string> HiddenInstanceIds { get; set; } = new();

        // Thumb stick orientation (a property of the hardware, shared by all profiles)
        // NubMouseEnabled/Buttons/Speed/Deadzone are now per profile; these copies are only read
        // once to migrate settings from v1.1.9 into existing profiles.
        public bool NubMouseEnabled { get; set; } = true;
        public bool NubMouseButtons { get; set; } = true;
        public double NubMouseSpeed { get; set; } = 1200;
        public double NubMouseDeadzone { get; set; } = 1.0;
        public int NubMouseRotation { get; set; } = 0;   // degrees clockwise: 0, 90, 180, 270
        public bool NubMouseInvertX { get; set; } = false;
        public bool NubMouseInvertY { get; set; } = false;
    }
}
