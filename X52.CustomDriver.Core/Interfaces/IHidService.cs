using System;
using X52.CustomDriver.Core.Models;

namespace X52.CustomDriver.Core.Interfaces
{
    public interface IHidService
    {
        bool IsConnected { get; }

        // Windows device instance ID of the connected stick, e.g. HID\VID_06A3&PID_075C\7&1A2B3C&0&0000
        string? DeviceInstanceId { get; }
        void Initialize();
        void StartListening();
        void StopListening();
        
        // LED and MFD Control
        void SetLed(byte ledId, byte state);
        void SetBrightness(byte brightness);
        
        // Calibration
        void ResetCalibration();
        void CalibrateCenter();
        
        event EventHandler<X52State> OnStateChanged;
        event EventHandler<string> OnError;

        // Raised when the stick is unplugged (the driver reconnects on its own when it comes back)
        event EventHandler? OnDisconnected;
    }
}
