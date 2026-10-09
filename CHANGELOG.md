# Changelog

Each `## vX.Y.Z` section below becomes the notes of the GitHub Release with that tag.

## v1.4.9

### Fixes
- **vJoy name only when it's the X52's alone**: Windows keeps one name for all vJoy devices (they share one hardware ID), so a single vJoy device can't be renamed. "Ærakon X52 Virtual Joystick" is now only used while the X52's device is the only vJoy device. With other vJoy devices (e.g. one another program uses), every entry keeps vJoy's own "vJoy Device", and v1.4.8's name is taken back off. The LIVE tab shows which vJoy device number the X52 uses.

## v1.4.8

### Fixes
- **vJoy name**: after SET UP VJOY, Game Controllers showed every vJoy device as plain "vJoy Device". Windows resets the name when vJoy rebuilds its devices; the driver now puts "Ærakon X52 Virtual Joystick" back (right after setup and on every start). Windows keeps one name for all vJoy devices, so every vJoy device carries it.
- The LIVE tab shows which vJoy device the X52 uses, e.g. "Ærakon X52 Virtual Joystick (vJoy device #2)".

## v1.4.7

Safety and reliability release, after a full review of the fork.

### Changed
- **Profiles are saved with SAVE** (no more autosave). Edits still work immediately; a ● marks profiles with unsaved changes (profile list, LIVE picker, tray menu, PROFILES tab). New **REVERT** button. Closing the driver with unsaved changes asks Save / Don't save / Cancel. Creating, duplicating and deleting profiles is still saved right away.
- **Ctrl+Alt+M** switches the thumb stick mouse on/off for the current session only; it no longer changes the profile.
- **Key mappings**: *Hold* now really holds the keys (no auto-repeat; games read the key state). New *Repeat* action for keyboard-style auto-repeat. Mappings from the original driver (v1.1.8) stay one-shot taps (or toggles) instead of turning into Hold.
- **vJoy setup asks** before changing vJoy device 1, and can instead create a **separate vJoy device just for the X52** (the default when device 1 is used by another program). The choice is remembered.

### Fixes
- **No more lost profiles**: profiles.json and settings.json are written safely (temp file + swap) with a `.bak` backup. A damaged file is kept as `*.corrupt-<time>.json` and the backup is used; you get a message.
- **Save problems are shown** in a message bar instead of failing silently. If the driver's folder is read-only (e.g. portable copy in Program Files), profiles go to `%LocalAppData%\AerakonX52Driver`.
- **HidHide**: uninstalling the driver makes the X52 visible to games again. If the driver was moved or reinstalled elsewhere and can't see the hidden X52, there is a **FIX ACCESS** button. HidHide's "inverted" mode is no longer switched silently; the driver asks first.
- **Only one driver runs at a time**: starting it again shows the running window (and the installer asks to close it).
- **Crash safety**: held keys and mouse buttons are released on any error, and errors are logged to `%LocalAppData%\AerakonX52Driver\crash.log`.
- **X52 Pro**: stick X/Y read with the Pro's 10-bit layout (from libx52). *Not tested on a real X52 Pro yet; please report how it works.* The connected model is shown on the LIVE tab. Standard X52: twist no longer picks up two wrong bits.
- Mouse wheel up/down now show up as buttons (in vJoy and for key mappings). The mode is kept while the mode dial is between positions. The first X52 revision (PID 0255) is recognised.

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
