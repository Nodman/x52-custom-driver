# Changelog

Each `## vX.Y.Z` section below becomes the notes of the GitHub Release with that tag.

## v1.4.6

First release of the community fork of [d2ndsky/x52-custom-driver](https://github.com/d2ndsky/x52-custom-driver) (based on its v1.1.8).

### Fixes
- **Every button is read correctly** on the X52 and X52 Pro (bit layout from libx52). The original never read most stick buttons on the standard X52 and read most X52 Pro buttons from the wrong bits.
- **Profiles are saved**: edits to curves and mappings used to go to a temporary copy and were lost on restart.
- **Safe unplug/replug**: no more runaway cursor or stuck keys; the driver reconnects automatically.
- **vJoy output rebuilt**: fixed numbering (31 buttons per mode), mode banks without stuck buttons, and the 8-way hat as a POV hat. Reset/Clutch/mouse buttons are no longer overwritten by hat directions.

### New
- **Thumb stick mouse**: cursor, left click, middle click and scroll, each switchable per profile; speed, deadzone, rotation and invert. Ctrl+Alt+M toggles it from anywhere.
- **Tabbed UI**: LIVE, PROFILES, SETTINGS.
- **Profiles**: switch automatically by game (exact name, several names, or wildcards; PICK… from running programs) or manually (LIVE tab, PROFILES, tray menu). New / duplicate / delete, autosave.
- **Key mappings**: Hold, Tap or Toggle per mapping; sent as scan codes so games see them; keyboard-style auto-repeat; the vJoy checkbox works.
- **Axis curves** for stick X/Y and twist (deadzone, curvature, saturation, invert) with a live graph.
- **LIVE tab**: top-down stick view (physical vs. after curves), POV hat view, named button grid with current vJoy numbers, mode selector in LED colours, key mapping activity.
- **One-click vJoy setup** (8 axes, 128 buttons, 1 continuous POV), offered on first start.
- **Hide the real X52 from games** with HidHide, offered on first start, so inputs don't arrive twice.
- **Installer** in English, Ukrainian and Spanish; checks for vJoy.
- Built-in profiles: Default, MSFS 2020, DCS World, Wardogs.

### Requirements
- Windows 10/11 x64 with the [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
- [vJoy](https://github.com/jshafer817/vJoy/releases) (required)
- [HidHide](https://github.com/nefarius/HidHide/releases) (recommended)
