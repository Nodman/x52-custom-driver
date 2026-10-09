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

        // Startup checks: remember a "No" so we don't ask again until something changes
        public string? DeclinedVJoySetupFor { get; set; }

        // vJoy device the X52 is sent to: 1, or a separate device created for the X52
        public uint VJoyDeviceId { get; set; } = 1;
   // vJoy config the user said No to, e.g. "18/0/1"
        public bool DeclinedHideRealX52 { get; set; } = false;

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
