using System;

namespace X52.CustomDriver.Core.Interfaces
{
    public interface IVJoyService
    {
        bool IsAvailable { get; }
        uint DeviceId { get; }
        string DeviceName { get; }
        
        bool Initialize(uint deviceId);
        void Shutdown();
        
        void SetAxisX(int value);
        void SetAxisY(int value);
        void SetAxisZ(int value);
        void SetRx(int value);
        void SetRy(int value);
        void SetRz(int value);
        void SetButtons(uint buttons);
        void SetButton(int buttonId, bool pressed);
        void SetSlider(int value, int index = 0);
        void SetDial(int value);

        // Capabilities of the vJoy device as configured in "Configure vJoy"
        int ButtonCount { get; }
        int ContinuousPovCount { get; }
        int DiscretePovCount { get; }

        /// <summary>
        /// State of any vJoy device (1-16): 0 = used by this driver, 1 = free, 2 = used by another
        /// program, 3 = doesn't exist, 4 = unknown (vJoy not installed or not working).
        /// </summary>
        int QueryDeviceStatus(uint id);

        /// <summary>Configured buttons / continuous POVs / 4-way POVs of any existing vJoy device.</summary>
        (int buttons, int contPovs, int discPovs) QueryDeviceLayout(uint id);

        /// <summary>Set POV hat 1. direction: -1 = centred, else degrees clockwise from up (0, 45, ... 315).</summary>
        void SetPov(int direction);
    }
}
