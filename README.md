# ClearShot

A fast, private screenshot tool for Windows.

- **No account, no uploads, no tracking.** Your screenshots never leave your PC. There is no ClearShot server, so there is nothing to hack.
- **One key, done.** Press a shortcut and the screenshot is saved and already on your clipboard, ready to paste.
- **True resolution.** 4K stays 4K, even on scaled displays.
- **Works in games.** Uses the same Windows capture system as OBS and Xbox Game Bar. Nothing is injected into the game.
- **HDR done right.** HDR screens are tone-mapped so screenshots look the way they did on screen, not washed out.

## Shortcuts

| Shortcut | What it does |
|---|---|
| Print Screen | Captures the whole monitor your mouse is on |
| Ctrl + Print Screen | Freezes the screen so you can drag a box around the part you want. Esc or right-click cancels. |

Change either one in **Settings** (right-click the tray icon).

Every capture is copied to the clipboard and saved as a PNG in `Pictures\ClearShot` (you can pick another folder).

### Print Screen doesn't work?

Windows 11 can keep Print Screen for its own Snipping Tool. Turn off **Settings > Accessibility > Keyboard > Use the Print screen key to open screen capture**, then restart ClearShot. ClearShot tells you if this happens.

## Install

Download `ClearShot.exe` from Releases and run it. No installer and no admin rights needed. Tick **Start ClearShot with Windows** in Settings if you want it always ready.

## Privacy

ClearShot makes no network connections. Settings live in `%APPDATA%\ClearShot\settings.json` and a small diagnostic log in `%APPDATA%\ClearShot\clearshot.log`. Both stay on your PC. The code is all here for anyone to check.

## Known limits

- In the rare game that uses true exclusive fullscreen, the region overlay can make the game minimise. The full-screen shortcut is unaffected.
- Captures are single-monitor (the one under your mouse).

## Build

Requires the .NET 10 SDK.

```
dotnet test
dotnet publish src/ClearShot/ClearShot.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish
```

Set `CLEARSHOT_LIVE=1` to also run the tests that capture the real screen.

## Licence

MIT
