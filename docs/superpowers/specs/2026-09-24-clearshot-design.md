# ClearShot: design (24/09/2026)

## Goal
A fast, private screenshot tool for Windows that beats Gyazo on trust: no account, no uploads, no telemetry. Free and open source on GitHub, funded by an optional donation link.

## Version 1 scope
- Two global shortcuts, each with one job (both rebindable):
  - Full screen (default Print Screen): captures the monitor under the mouse at native resolution, instantly.
  - Region (default Ctrl + Print Screen): freezes that monitor and shows a drag box; Esc or right-click cancels.
- Works in games: capture uses the DXGI Desktop Duplication API (the same OS path as OBS and Game Bar). Nothing is injected into the game process.
- HDR: when the monitor is in HDR, the frame is captured as FP16 scRGB and tone-mapped to an SDR PNG that looks right anywhere it is pasted.
- After every capture: copied to the clipboard, saved as PNG in the chosen folder, a short shutter sound (optional), and a small no-focus preview in the corner (optional; click opens the file).
- Tray app, single instance, optional start with Windows, settings stored in %APPDATA%\ClearShot\settings.json.
- Donation link in the tray menu. No other network access.

## Out of scope for v1
Window capture, annotation, GIF/video, resizing, other file formats, uploads of any kind.

## Architecture (C#, .NET 10, WinForms, Vortice DXGI/D3D11 bindings)
- `Capture/ScreenCapturer`: finds the DXGI output for a monitor, duplicates it, copies one frame to a staging texture, converts to a Bitmap. Falls back to GDI capture if duplication fails (secure desktop, unsupported adapter).
- `Capture/HdrToneMapper`: pure function, scRGB linear FP16 to sRGB 8-bit. SDR white from Windows' SDR brightness setting maps to 1.0; highlights above a knee roll off smoothly up to the image's 99.5th percentile luminance.
- `Capture/DisplayInfo`: reads the SDR white level via DisplayConfig.
- `HotkeyManager`: RegisterHotKey on a hidden message window; reports registration failures (for example Windows' own "Print Screen opens Snipping Tool" setting).
- `RegionSelector`: full-monitor borderless overlay drawn from the frozen frame at 1:1 physical pixels.
- `Output`: unique filenames, PNG save, clipboard (bitmap + PNG formats).
- `PreviewToast`, `ShutterSound`, `SettingsForm`, `TrayApp`.

## Error handling
Capture failure shows a tray balloon and never crashes the app. Hotkey conflicts are reported with the fix. Corrupt settings fall back to defaults.

## Known limit
In the rare game that uses true exclusive fullscreen, the region overlay can make the game minimise. The full-screen shortcut is unaffected.

## Testing
Unit tests for the tone mapper, hotkey parsing, filenames and settings. Manual check by the user on SDR and HDR desktops and in a game.
