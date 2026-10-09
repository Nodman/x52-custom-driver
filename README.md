# Ærakon x52 driver – community fork 💎

> **This is a fork** of [d2ndsky/x52-custom-driver](https://github.com/d2ndsky/x52-custom-driver).
> It fixes button decoding (most stick buttons were never read on the standard X52), and adds:
> thumb stick mouse, per-game profiles with Hold/Tap/Toggle key mappings, axis curves with live preview,
> proper vJoy output (31 buttons × 3 mode banks + 8-way POV), one-click vJoy setup,
> hiding the real X52 from games via HidHide, and safe unplug/replug.
> Installers are built automatically by GitHub Actions from this repository's code.

![Æ52 Icon](app_icon.png)

A modern, high-performance, and open-source driver for the **Logitech/Saitek X52 (Pro & Standard)**. Designed to replace the obsolete SST software with a focus on stability, precision, and hardware rescue.

## 🚀 Key Features

- **🛡️ Hardware Rescue (Silver Bullet Logic)**:
  - **Ghostbuster Filter**: Eliminates random inputs from the Mouse Nub (common hardware failure).
  - **Amputation Y**: Disables faulty nub axes to restore 100% functionality to **Hat 2**.
- **📈 Advanced Sensitivity Control**:
  - Independent **X and Y axis** multipliers (0.1x to 3.0x).
  - Real-time adjustment with visual feedback.
- **🎨 Premium Neon UI**:
  - Live hardware monitor for all 32 buttons and 7 axes.
  - Dynamic Mode Indicator synced with the physical X52 dial.
- **🔄 Smart Profiling**:
  - Auto-load profiles based on the running game executable (e.g., `DCS.exe`).
  - Native 64-bit .NET 9 performance.
- **📦 Ultra-lightweight**:
  - ~6MB installer compared to the hundreds of MBs of the original software.
  - Easy installation and uninstallation with custom "Æ52" branding.

## 🗂️ Profiles

The window now has three tabs: **LIVE** (axes, buttons, status), **PROFILES** and **SETTINGS**.
Each profile has its own button mappings, axis curves (deadzone, curvature, saturation, invert for stick X/Y and twist) and thumb stick mouse settings,
and switches on automatically when its game .exe is running. Everything is saved automatically.

## 🖱️ Thumb Stick Mouse

The small rubber thumb stick on the throttle now works as a real Windows mouse, like it did with the original Saitek driver:

- Thumb stick moves the cursor (adjustable speed and deadzone)
- Throttle mouse button = left click, wheel press = middle click, scroll wheel = mouse wheel
- Can be switched off in the **THUMB STICK MOUSE** panel, or from anywhere with **Ctrl+Alt+M**
- Unplugging the stick stops the cursor and releases buttons; the driver reconnects automatically when it is plugged back in
- Raise the deadzone if a worn thumb stick makes the cursor drift on its own

## 🛠️ Requirements

1. **vJoy**: This driver sends data to a virtual joystick.
   - Download and install vJoy from [vJoy Official Site](http://vjoystick.sourceforge.net/).
   - Ensure **Device #1** is enabled in "Configure vJoy".
2. **.NET 9 Runtime**: Usually included with Windows 11 or installed automatically by the app.

## 💻 Installation

1. Download the installer from the latest successful run under **Actions** (artifact *AerakonX52-installer*).
2. Run the installer.
3. Open the app and ensure the "vJoy Status" indicator is green.

## 🕹️ Why use this instead of the original?

The original Logitech software is over 20 years old and often conflicts with modern Windows security features (like Memory Integrity). This driver communicates directly with the HID raw data, ensuring zero lag, zero crashes, and solving the "drifting mouse" problem that plagues old X52 units.

## 🤝 Contributing

This is a **Platinum Release** of the Ærakon driver. Contributions are welcome for adding MFD control or more advanced macro features.

---
*Created with ❤️ by d2ndsky / Ærakon*

## 🔢 Versioning

The version lives in one place: the `VERSION` file in the repository root.
The app (window title, file properties), the installer (title and file name) and the build artifacts all read it.
To release a new version, change `VERSION` and push.

