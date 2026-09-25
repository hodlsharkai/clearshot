using System.Drawing.Imaging;
using System.Runtime;
using ClearShot.Capture;

namespace ClearShot;

internal sealed class TrayApp : ApplicationContext
{
    private const int FullScreenId = 1, RegionId = 2, EscapeId = 3, GifId = 4;

    private static readonly TimeSpan GifMaxLength = TimeSpan.FromSeconds(15);
    private const long DiscordFreeLimitBytes = 10 * 1024 * 1024;
    private CancellationTokenSource? _gifStop;
    private Action? _cancelSelection;

    private readonly Settings _settings = Settings.Load();
    private readonly HotkeyManager _hotkeys = new();
    private readonly ShutterSound _sound = new();
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _fullScreenItem = new("Capture full screen");
    private readonly ToolStripMenuItem _regionItem = new("Capture region");
    private readonly ToolStripMenuItem _gifItem = new("Record a GIF");
    // A hidden control, so signals from other threads (a second copy of ClearShot starting) reach the UI thread.
    private readonly Control _uiThread = new();
    private readonly RegisteredWaitHandle _showWait;
    private PreviewToast? _toast;
    private SettingsForm? _settingsForm;
    private bool _busy;
    private string _lastAnnouncedProblem = "";

    /// <param name="showSignal">Set when ClearShot is started again while already running: open the window.</param>
    /// <param name="openWindow">Open the window now (false when Windows starts ClearShot at sign-in).</param>
    public TrayApp(EventWaitHandle showSignal, bool openWindow)
    {
        Theme.Apply(_settings.Theme);
        _uiThread.CreateControl();
        _showWait = ThreadPool.RegisterWaitForSingleObject(showSignal,
            (_, _) => _uiThread.BeginInvoke(ShowWindow), null, Timeout.Infinite, executeOnlyOnce: false);

        _fullScreenItem.Click += async (_, _) => await FromMenu(region: false);
        _regionItem.Click += async (_, _) => await FromMenu(region: true);
        _gifItem.Click += async (_, _) => { await Task.Delay(250); await RecordGif(); };

        var menu = new ContextMenuStrip();
        var openItem = new ToolStripMenuItem("Open ClearShot", null, (_, _) => ShowWindow()) { Font = new Font(menu.Font, FontStyle.Bold) };
        menu.Items.Add(openItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_fullScreenItem);
        menu.Items.Add(_regionItem);
        menu.Items.Add(_gifItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open screenshots folder", null, (_, _) => OpenFolder(_settings.SaveFolder));
        if (AppInfo.ActiveDonations.Count > 0)
            menu.Items.Add("Buy me a beer", null, (_, _) => DonateForm.ShowFor(AppInfo.ActiveDonations));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => ExitThread());

        _tray = new NotifyIcon
        {
            Icon = LoadAppIcon(SystemInformation.SmallIconSize.Width),
            Text = AppInfo.Name,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };

        _hotkeys.Pressed += async id =>
        {
            if (id == EscapeId) { _cancelSelection?.Invoke(); _gifStop?.Cancel(); return; }
            if (id == GifId) { await RecordGif(); return; }
            await Capture(region: id == RegionId);
        };
        Task.Run(ScreenCapturer.WarmUp);
        _ = MediaPauser.WarmUpAsync();
        RegisterHotkeys(announceProblems: true);

        if (!_settings.WelcomeShown)
        {
            _settings.WelcomeShown = true;
            TrySaveSettings();
            _tray.ShowBalloonTip(6000, "ClearShot is running",
                $"{HotkeyText(_settings.FullScreenHotkey)}: full screen\n{HotkeyText(_settings.RegionHotkey)}: drag to pick an area\nClick the tray icon to open ClearShot.",
                ToolTipIcon.None);
        }
        if (openWindow) _uiThread.BeginInvoke(ShowWindow);
    }

    private static string HotkeyText(string text) => Hotkey.TryParse(text, out var hk) ? hk.DisplayText : text;

    private void RegisterHotkeys(bool announceProblems)
    {
        var failed = new List<string>();
        Register(FullScreenId, _settings.FullScreenHotkey, _fullScreenItem, failed);
        Register(RegionId, _settings.RegionHotkey, _regionItem, failed);
        Register(GifId, _settings.GifHotkey, _gifItem, failed);
        // Only speak up once per problem, not every time the window is opened or a box is clicked.
        var problem = string.Join("|", failed);
        if (problem == _lastAnnouncedProblem) return;
        _lastAnnouncedProblem = problem;
        if (failed.Count == 0 || !announceProblems) return;

        var names = string.Join(" and ", failed);
        bool printScreenInvolved = failed.Any(f => f.Contains("Print Screen"));
        var message = $"ClearShot couldn't use {names} because another app or Windows already uses it.";
        if (printScreenInvolved)
        {
            message += "\n\nWindows 11 often keeps Print Screen for its own Snipping Tool. Turn off " +
                       "\"Use the Print screen key to open screen capture\" in Settings > Accessibility > Keyboard, then restart ClearShot." +
                       "\n\nOpen that Windows setting now?";
            if (MessageBox.Show(message, AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                AppInfo.OpenUrl("ms-settings:easeofaccess-keyboard");
        }
        else
        {
            MessageBox.Show(message + "\n\nPick a different shortcut in ClearShot settings.", AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void Register(int id, string text, ToolStripMenuItem item, List<string> failed)
    {
        if (!Hotkey.TryParse(text, out var hotkey) || !hotkey.IsSafeAsGlobalShortcut)
        {
            failed.Add($"\"{text}\"");
            item.ShortcutKeyDisplayString = "";
            return;
        }
        item.ShortcutKeyDisplayString = hotkey.DisplayText;
        if (!_hotkeys.Register(id, hotkey))
        {
            Log.Write($"Hotkey {hotkey} could not be registered");
            failed.Add(hotkey.DisplayText);
        }
    }

    private async Task FromMenu(bool region)
    {
        // Let the menu finish fading so it isn't in the shot.
        await Task.Delay(250);
        await Capture(region);
    }

    private async Task Capture(bool region)
    {
        if (_busy) return;
        _busy = true;
        CaptureResult? shot = null;
        Bitmap? cropped = null;
        try
        {
            var cursor = Cursor.Position;
            bool wantHdr = _settings.SaveHdrJxr || _settings.SaveHdrPng;

            // Live region (the default): pick the box on the moving screen, then capture once the overlay is gone.
            Rectangle? liveBox = null;
            var capturePoint = cursor;
            if (region && !_settings.FreezeWhileSelecting)
            {
                var monitor = Screen.FromPoint(cursor).Bounds;
                using var live = new LiveRegionSelector(monitor);
                var chosen = await SelectWithEscape(live.SelectAsync, live.Cancel);
                if (chosen is not Rectangle box) return;
                liveBox = box with { X = box.X + monitor.X, Y = box.Y + monitor.Y };
                capturePoint = new Point(monitor.X + monitor.Width / 2, monitor.Y + monitor.Height / 2);
                // Let Windows draw a frame without the overlay before capturing.
                await Task.Run(() => { DwmFlush(); DwmFlush(); });
            }

            shot = await Task.Run(() => ScreenCapturer.CaptureMonitorAt(capturePoint, wantHdr));
            var image = shot.Image;
            var hdr = shot.Hdr;
            var areaOnScreen = shot.Bounds;

            if (region)
            {
                Rectangle selection;
                if (liveBox is Rectangle onScreen)
                {
                    selection = onScreen with { X = onScreen.X - shot.Bounds.X, Y = onScreen.Y - shot.Bounds.Y };
                    selection.Intersect(new Rectangle(Point.Empty, shot.Image.Size));
                    if (selection.Width < 1 || selection.Height < 1) return;
                }
                else
                {
                    // Frozen region: drag over the still picture taken when the shortcut was pressed.
                    using var selector = new RegionSelector(shot.Image, shot.Bounds);
                    if (await SelectWithEscape(selector.SelectAsync, selector.Cancel) is not Rectangle frozenBox) return;
                    selection = frozenBox;
                }
                cropped = shot.Image.Clone(selection, PixelFormat.Format32bppArgb);
                image = cropped;
                hdr = hdr?.Crop(selection);
            }

            if (_settings.PlaySound) _sound.Play();

            Directory.CreateDirectory(_settings.SaveFolder);
            var path = FileNamer.UniquePath(_settings.SaveFolder, DateTime.Now);
            var png = await Task.Run(() => ClipboardOutput.EncodePng(image));
            await File.WriteAllBytesAsync(path, png);
            ClipboardOutput.Copy(image, png);

            if (_settings.ShowPreview) ShowPreview(image, path, areaOnScreen, hdrCopy: hdr is not null);
            if (hdr is not null) await SaveHdrCopies(hdr, path);
        }
        catch (Exception ex)
        {
            Log.Write($"Capture failed: {ex}");
            _tray.ShowBalloonTip(5000, "Screenshot failed", ex.Message, ToolTipIcon.Warning);
        }
        finally
        {
            cropped?.Dispose();
            shot?.Dispose();
            _busy = false;
            // A 4K capture briefly needs a few hundred MB; hand it back now rather than sitting on it in the tray.
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();
        }
    }

    /// <summary>
    /// Press the GIF shortcut to pick an area and start recording; press it again (or Esc, or Stop) to finish.
    /// The GIF is saved and copied as a file, so pasting into Discord uploads the animation.
    /// </summary>
    private async Task RecordGif()
    {
        if (_gifStop is not null) { _gifStop.Cancel(); return; }
        if (_busy) return;
        _busy = true;
        try
        {
            var cursor = Cursor.Position;
            var monitor = Screen.FromPoint(cursor).Bounds;
            Rectangle? chosen;
            using (var live = new LiveRegionSelector(monitor))
                chosen = await SelectWithEscape(live.SelectAsync, live.Cancel);
            if (chosen is not Rectangle box) return;
            await Task.Run(() => { DwmFlush(); DwmFlush(); });

            var hmonitor = ScreenCapturer.MonitorFromPoint(new Point(monitor.X + monitor.Width / 2, monitor.Y + monitor.Height / 2), 2);
            var onScreen = box with { X = box.X + monitor.X, Y = box.Y + monitor.Y };
            // Standard: small and quick, ideal for Discord. High: bigger, smoother, dithered colours.
            bool high = _settings.GifQuality == "High";
            int fps = high ? 30 : 15, maxWidth = high ? 1920 : 960;

            _gifStop = new CancellationTokenSource();
            bool escapeHooked = Hotkey.TryParse("Escape", out var esc) && _hotkeys.Register(EscapeId, esc);
            using var overlay = new RecordingOverlay(onScreen, monitor, GifMaxLength);
            overlay.StopClicked += () => _gifStop?.Cancel();
            overlay.Show();
            Recording recording;
            try
            {
                recording = await new RegionRecorder(hmonitor, box, fps, maxWidth, GifMaxLength).RunAsync(_gifStop.Token);
            }
            finally
            {
                if (escapeHooked) _hotkeys.Unregister(EscapeId);
                _gifStop.Dispose();
                _gifStop = null;
            }

            Directory.CreateDirectory(_settings.SaveFolder);
            var path = FileNamer.UniquePath(_settings.SaveFolder, DateTime.Now, ".gif");
            using var first = FirstFrame(recording); // before encoding releases the frames
            bool mp4Saved = false;
            if (_settings.SaveMp4)
            {
                // The MP4 goes first: it reads the frames, and the GIF step frees them as it goes.
                overlay.ShowSaving("Making your MP4…");
                try
                {
                    await Mp4Maker.SaveAsync(recording, Path.ChangeExtension(path, ".mp4"));
                    mp4Saved = true;
                }
                catch (Exception ex)
                {
                    Log.Write($"MP4 save failed: {ex}");
                }
            }
            overlay.ShowSaving("Making your GIF…");
            await Task.Run(() => GifMaker.Save(recording, path, dither: high));
            ClipboardOutput.CopyFile(path);
            if (_settings.PlaySound) _sound.Play();

            long bytes = new FileInfo(path).Length;
            var caption = $"GIF copied and saved  ·  {recording.TotalMs / 1000.0:0.0} s  ·  {bytes / 1048576.0:0.0} MB";
            if (mp4Saved) caption += "  ·  MP4 too";
            else if (_settings.SaveMp4) caption += "  ·  MP4 failed";
            if (recording.StoppedEarly) caption += "  ·  stopped early (memory limit)";
            if (bytes > DiscordFreeLimitBytes) caption += "  ·  over Discord's 10 MB free limit";
            if (_settings.ShowPreview) ShowPreview(first, path, monitor, hdrCopy: false, caption);
        }
        catch (Exception ex)
        {
            Log.Write($"GIF recording failed: {ex}");
            _tray.ShowBalloonTip(5000, "GIF failed", ex.Message, ToolTipIcon.Warning);
        }
        finally
        {
            _busy = false;
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();
        }
    }

    private static Bitmap FirstFrame(Recording recording)
    {
        var bitmap = new Bitmap(recording.Width, recording.Height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, recording.Width, recording.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < recording.Height; y++)
                System.Runtime.InteropServices.Marshal.Copy(recording.Frames[0].Bgra, y * recording.Width * 4, data.Scan0 + y * data.Stride, recording.Width * 4);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }

    /// <summary>
    /// Runs a region picker. The pickers never take focus, so Esc is caught as a global shortcut while one is up,
    /// and media is paused around it if that option is on.
    /// </summary>
    private async Task<Rectangle?> SelectWithEscape(Func<Task<Rectangle?>> select, Action cancel)
    {
        var pausing = _settings.PauseMediaWhileSelecting ? MediaPauser.PausePlayingAsync() : null;
        _cancelSelection = cancel;
        bool escapeHooked = Hotkey.TryParse("Escape", out var esc) && _hotkeys.Register(EscapeId, esc);
        try
        {
            return await select();
        }
        finally
        {
            _cancelSelection = null;
            if (escapeHooked) _hotkeys.Unregister(EscapeId);
            if (pausing is not null) _ = MediaPauser.ResumeAsync(await pausing);
        }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    private async Task SaveHdrCopies(HdrFrame hdr, string pngPath)
    {
        var stem = pngPath[..^".png".Length];
        var failures = new List<string>();
        if (_settings.SaveHdrJxr)
        {
            try { await Task.Run(() => HdrWriters.WriteJxr(hdr, stem + ".jxr")); }
            catch (Exception ex) { Log.Write($"JXR save failed: {ex}"); failures.Add(".jxr"); }
        }
        if (_settings.SaveHdrPng)
        {
            try { await Task.Run(() => HdrWriters.WritePqPng(hdr, stem + " HDR.png")); }
            catch (Exception ex) { Log.Write($"HDR PNG save failed: {ex}"); failures.Add("HDR PNG"); }
        }
        if (failures.Count > 0)
            _tray.ShowBalloonTip(5000, "HDR copy not saved", $"The normal screenshot is fine, but the {string.Join(" and ", failures)} copy couldn't be saved.", ToolTipIcon.Warning);
    }

    private void ShowPreview(Bitmap image, string path, Rectangle monitorBounds, bool hdrCopy, string? caption = null)
    {
        _toast?.Close();
        _toast = new PreviewToast(image, path, monitorBounds, hdrCopy, caption);
        _toast.FormClosed += (sender, _) =>
        {
            if (ReferenceEquals(_toast, sender)) _toast = null;
            ((Form)sender!).Dispose();
        };
        _toast.Show();
    }

    private void ShowWindow() => ShowWindow(draft: null, location: null);

    private void ShowWindow(SettingsForm.Draft? draft, Point? location)
    {
        if (_settingsForm is not null)
        {
            if (_settingsForm.WindowState == FormWindowState.Minimized) _settingsForm.WindowState = FormWindowState.Normal;
            _settingsForm.Activate();
            return;
        }
        _settingsForm = new SettingsForm(_settings, draft);
        if (location is Point at)
        {
            _settingsForm.StartPosition = FormStartPosition.Manual;
            _settingsForm.Location = at;
        }
        // A new appearance applies straight away; controls only pick up a theme when they're created,
        // so the window is rebuilt in place.
        _settingsForm.ThemePicked += theme => _uiThread.BeginInvoke(() =>
        {
            if (_settingsForm is not { } open) return;
            var keep = open.CaptureDraft();
            var where = open.Location;
            _settings.Theme = theme;
            TrySaveSettings();
            Theme.Apply(theme);
            open.Close();
            ShowWindow(keep, where);
        });
        _settingsForm.SettingsChanged += TrySaveSettings;
        // While a shortcut box is recording, pressing a shortcut should record it, not take a screenshot.
        _settingsForm.RecordingShortcut += recording =>
        {
            if (recording) _hotkeys.UnregisterAll();
            else RegisterHotkeys(announceProblems: true);
        };
        _settingsForm.FormClosed += (_, _) =>
        {
            _settingsForm.Dispose();
            _settingsForm = null;
            RegisterHotkeys(announceProblems: true);
        };
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private void TrySaveSettings()
    {
        try { _settings.Save(); }
        catch (Exception ex) { Log.Write($"Could not save settings: {ex.Message}"); }
    }

    public static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start("explorer.exe", $"\"{folder}\"");
        }
        catch (Exception ex)
        {
            Log.Write($"Could not open folder: {ex.Message}");
        }
    }

    public static Icon LoadAppIcon(int size)
    {
        using var stream = typeof(TrayApp).Assembly.GetManifestResourceStream("clearshot.ico");
        return stream is null ? SystemIcons.Application : new Icon(stream, size, size);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _showWait.Unregister(null);
            _uiThread.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _hotkeys.Dispose();
            _sound.Dispose();
            _toast?.Dispose();
        }
        base.Dispose(disposing);
    }
}
