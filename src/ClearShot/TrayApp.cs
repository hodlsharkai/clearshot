using System.Drawing.Imaging;
using System.Runtime;
using ClearShot.Capture;

namespace ClearShot;

internal sealed class TrayApp : ApplicationContext
{
    private const int FullScreenId = 1, RegionId = 2;

    private readonly Settings _settings = Settings.Load();
    private readonly HotkeyManager _hotkeys = new();
    private readonly ShutterSound _sound = new();
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _fullScreenItem = new("Capture full screen");
    private readonly ToolStripMenuItem _regionItem = new("Capture region");
    // A hidden control, so signals from other threads (a second copy of ClearShot starting) reach the UI thread.
    private readonly Control _uiThread = new();
    private readonly RegisteredWaitHandle _showWait;
    private PreviewToast? _toast;
    private SettingsForm? _settingsForm;
    private bool _busy;

    /// <param name="showSignal">Set when ClearShot is started again while already running: open the window.</param>
    /// <param name="openWindow">Open the window now (false when Windows starts ClearShot at sign-in).</param>
    public TrayApp(EventWaitHandle showSignal, bool openWindow)
    {
        _uiThread.CreateControl();
        _showWait = ThreadPool.RegisterWaitForSingleObject(showSignal,
            (_, _) => _uiThread.BeginInvoke(ShowWindow), null, Timeout.Infinite, executeOnlyOnce: false);

        _fullScreenItem.Click += async (_, _) => await FromMenu(region: false);
        _regionItem.Click += async (_, _) => await FromMenu(region: true);

        var menu = new ContextMenuStrip();
        var openItem = new ToolStripMenuItem("Open ClearShot", null, (_, _) => ShowWindow()) { Font = new Font(menu.Font, FontStyle.Bold) };
        menu.Items.Add(openItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_fullScreenItem);
        menu.Items.Add(_regionItem);
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

        _hotkeys.Pressed += async id => await Capture(region: id == RegionId);
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
        if (!Hotkey.TryParse(text, out var hotkey))
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
            shot = await Task.Run(() => ScreenCapturer.CaptureMonitorAt(cursor, wantHdr));
            var image = shot.Image;
            var hdr = shot.Hdr;
            var areaOnScreen = shot.Bounds;

            if (region)
            {
                // Pause in parallel with showing the overlay so the box appears without delay.
                var pausing = _settings.PauseMediaWhileSelecting ? MediaPauser.PausePlayingAsync() : null;
                Rectangle? selection;
                try
                {
                    using var selector = new RegionSelector(shot.Image, shot.Bounds);
                    selection = selector.ShowDialog() == DialogResult.OK ? selector.Selection : null;
                }
                finally
                {
                    if (pausing is not null) _ = MediaPauser.ResumeAsync(await pausing);
                }
                if (selection is null) return;
                cropped = shot.Image.Clone(selection.Value, PixelFormat.Format32bppArgb);
                image = cropped;
                hdr = hdr?.Crop(selection.Value);
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

    private void ShowPreview(Bitmap image, string path, Rectangle monitorBounds, bool hdrCopy)
    {
        _toast?.Close();
        _toast = new PreviewToast(image, path, monitorBounds, hdrCopy);
        _toast.FormClosed += (sender, _) =>
        {
            if (ReferenceEquals(_toast, sender)) _toast = null;
            ((Form)sender!).Dispose();
        };
        _toast.Show();
    }

    private void ShowWindow()
    {
        if (_settingsForm is not null)
        {
            if (_settingsForm.WindowState == FormWindowState.Minimized) _settingsForm.WindowState = FormWindowState.Normal;
            _settingsForm.Activate();
            return;
        }
        _settingsForm = new SettingsForm(_settings);
        // While a shortcut box is recording, pressing a shortcut should record it, not take a screenshot.
        _settingsForm.RecordingShortcut += recording =>
        {
            if (recording) _hotkeys.UnregisterAll();
            else RegisterHotkeys(announceProblems: false);
        };
        _settingsForm.FormClosed += (_, _) =>
        {
            bool saved = _settingsForm.DialogResult == DialogResult.OK;
            _settingsForm.Dispose();
            _settingsForm = null;
            if (saved) TrySaveSettings();
            RegisterHotkeys(announceProblems: saved);
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
