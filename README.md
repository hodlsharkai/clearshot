# ClearShot

A fast, private screenshot tool for Windows.

- **No account, no uploads, no tracking.** Your screenshots never leave your PC. There is no ClearShot server, so there is nothing to hack.
- **One key, done.** Press a shortcut and the screenshot is saved and already on your clipboard, ready to paste.
- **Instant GIFs.** Drag a box, record, stop: a looping GIF is saved and copied, so pasting into Discord uploads the animation.
- **True resolution.** 4K stays 4K, even on scaled displays.
- **Works in games.** Uses the same Windows capture system as OBS and Xbox Game Bar. Nothing is injected into the game.
- **HDR done right.** HDR screens are tone-mapped so screenshots look the way they did on screen, not washed out. Optional HDR mode also saves true HDR copies.

## Shortcuts

| Shortcut | What it does |
|---|---|
| Alt + C | Captures the whole monitor your mouse is on |
| Alt + Shift + C | Drag a box around the part you want, on the live screen. Esc or right-click cancels. |
| Alt + G | Record a GIF: drag a box, then press Alt + G again (or Esc, or Stop) to finish. Up to 15 seconds. |

Change either one in **Settings** (right-click the tray icon).

Every capture is copied to the clipboard and saved as a PNG in `Pictures\ClearShot` (you can pick another folder).

### Want to use Print Screen?

Set it in Settings. Windows 11 may keep Print Screen for its own Snipping Tool; if so, ClearShot tells you and offers to open **Settings > Accessibility > Keyboard > Use the Print screen key to open screen capture** so you can turn it off.

## HDR mode

When your screen is in HDR, ClearShot can also save true HDR copies next to the normal PNG. Turn on either or both in the ClearShot window:

- **.jxr**: the format Xbox Game Bar uses. Opens in full HDR in Windows Photos.
- **HDR PNG**: 16-bit PNG in the HDR10 colour space. Shows in full HDR in Chrome and Edge.

The normal PNG is still what gets copied to the clipboard, because most apps can't show HDR.

## GIF quality

In the ClearShot window, under **GIFs**:

- **Standard** (default): up to 960 px wide at 15 fps. Small files, ideal for Discord.
- **High**: up to 1920 px wide at 30 fps. Bigger files.
- For game and video footage, tick **Also save an MP4**: GIFs are limited to 256 colours per frame, MP4s aren't.

ClearShot has its own GIF encoder, built for smooth, steady animation of screen and game footage: pixels that haven't changed are left alone (so still areas can't flicker), each frame gets its own palette with extra room for dark shades and always a true black, and redrawn pixels are gently error-diffusion dithered. On dark game footage it was tested against gifski (maximum quality) and FFmpeg and had the fewest blotches and a quarter of FFmpeg's flicker, at a similar file size.
- **Also save an MP4**: saves an MP4 next to the GIF, in full colour and much smaller. The GIF is still what gets copied.

## Download

**[Download the latest ClearShot.exe](https://github.com/hodlsharkai/ClearShot/releases/latest)** and run it. No installer and no admin rights needed.

Windows may say it "protected your PC" because the app isn't code-signed yet. Click **More info**, then **Run anyway**.

Open ClearShot again at any time (or click its tray icon) to change the save folder, shortcuts and options. Tick **Start ClearShot with Windows** if you want it always ready.

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

Releases are built by GitHub Actions: pushing a tag such as `v1.1.0` builds, tests and publishes `ClearShot.exe` to Releases.

## Buy me a beer

ClearShot is free, with no ads and no accounts. If it's useful to you, a tip keeps it going. Thank you!

| Network | Address |
|---|---|
| Bitcoin (BTC) | `bc1qqz7xqmkgesmstyj40xadrf3szzfjdr2gx4u86y` |
| Ethereum (ETH, plus Base, Arbitrum, Optimism, Polygon and BNB Chain, and tokens like USDC and USDT on them) | `0xB78c5D07b6F957168998315E49210074Dc55F179` |
| Solana (SOL, USDC and other Solana tokens) | `TC3YtercaL9u6fVfDCpe6hxW48rzV4zg5DKi1vDjzuM` |

Only send each coin to its matching network. The same addresses, with QR codes, are under **Buy me a beer** in the ClearShot window.

## Licence

MIT

ClearShot uses [ImageSharp](https://github.com/SixLabors/ImageSharp) to choose GIF palettes, licensed to this open-source project under the Apache License 2.0 (Six Labors Split License), plus [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) (MIT) and [QRCoder](https://github.com/codebude/QRCoder) (MIT).
