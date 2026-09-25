namespace ClearShot;

/// <summary>ClearShot's main window: shortcuts, save folder and options.</summary>
internal sealed class SettingsForm : Form
{
    private readonly Settings _settings;
    private readonly TextBox _folder = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly HotkeyBox _fullScreen = new() { Dock = DockStyle.Fill };
    private readonly HotkeyBox _region = new() { Dock = DockStyle.Fill };
    private readonly HotkeyBox _gif = new() { Dock = DockStyle.Fill };
    private readonly HotkeyBox _edit = new() { Dock = DockStyle.Fill };
    private readonly HotkeyBox _gifEdit = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _controller = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    private readonly CheckBox _sound = new() { Text = "Play a shutter sound", AutoSize = true, Margin = CheckMargin };
    private readonly CheckBox _preview = new() { Text = "Show a small preview after each capture", AutoSize = true, Margin = CheckMargin };
    private readonly CheckBox _freeze = new() { Text = "Freeze the screen while picking a region", AutoSize = true, Margin = CheckMargin };
    private readonly CheckBox _pauseMedia = new() { Text = "Pause videos and music while picking a region", AutoSize = true, Margin = CheckMargin };
    private readonly ComboBox _gifQuality = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly CheckBox _saveMp4 = new() { Text = "Also save an MP4 (full colour, much smaller file)", AutoSize = true, Margin = CheckMargin };
    private readonly CheckBox _hdrJxr = new() { Text = "Save a .jxr copy (opens in HDR in Windows Photos)", AutoSize = true, Margin = CheckMargin };
    private readonly CheckBox _hdrPng = new() { Text = "Save an HDR PNG copy (shows in HDR in Chrome and Edge)", AutoSize = true, Margin = CheckMargin };
    private readonly CheckBox _startup = new() { Text = "Start ClearShot with Windows", AutoSize = true, Margin = CheckMargin };
    private readonly ComboBox _theme = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly Button _closeButton = new() { Text = "Close", AutoSize = true, MinimumSize = new Size(88, 0), DialogResult = DialogResult.Cancel };

    // Tick boxes draw from their very edge, while label text has a little built-in padding; shift them
    // right so boxes, headings and labels share one left edge.
    private static readonly Padding CheckMargin = new(10, 3, 3, 3);

    /// <summary>True while a shortcut box is waiting for keys, so the real shortcuts should be switched off.</summary>
    public event Action<bool>? RecordingShortcut;

    /// <summary>Raised whenever a setting changes. Every change is saved straight away; there is no Save button.</summary>
    public event Action? SettingsChanged;

    /// <summary>Raised the moment a different appearance is picked, with its value ("System", "Light" or "Dark").</summary>
    public event Action<string>? ThemePicked;

    /// <summary>What's currently in the window, saved or not, so it can be carried over when the window is rebuilt.</summary>
    internal sealed record Draft(Settings Values, bool StartWithWindows);

    /// <param name="draft">Unsaved values to show instead of the saved ones (used when switching appearance).</param>
    public SettingsForm(Settings settings, Draft? draft = null)
    {
        _settings = settings;
        Text = AppInfo.Name;
        Icon = TrayApp.LoadAppIcon(32);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Font = new Font("Segoe UI", 9f);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(20, 16, 20, 16);

        var shown = draft?.Values ?? settings;
        _folder.Text = shown.SaveFolder;
        _fullScreen.Value = Parse(shown.FullScreenHotkey, "Alt+C");
        _region.Value = Parse(shown.RegionHotkey, "Alt+Shift+C");
        _gif.Value = Parse(shown.GifHotkey, "Alt+G");
        _edit.Value = Parse(shown.EditHotkey, "Alt+Shift+E");
        _gifEdit.Value = Parse(shown.GifEditHotkey, "Alt+Shift+G");
        _controller.Items.AddRange(ControllerShortcut.Choices.Select(c => c.Label).ToArray());
        _controller.SelectedIndex = Math.Max(0, Array.FindIndex(ControllerShortcut.Choices, c => c.Value == shown.ControllerShortcut));
        _sound.Checked = shown.PlaySound;
        _preview.Checked = shown.ShowPreview;
        _pauseMedia.Checked = shown.PauseMediaWhileSelecting;
        _freeze.Checked = shown.FreezeWhileSelecting;
        _gifQuality.Items.AddRange(["Standard", "High"]);
        _gifQuality.SelectedIndex = shown.GifQuality == "High" ? 1 : 0;
        _saveMp4.Checked = shown.SaveMp4;
        _hdrJxr.Checked = shown.SaveHdrJxr;
        _hdrPng.Checked = shown.SaveHdrPng;
        _startup.Checked = draft?.StartWithWindows ?? StartupRegistration.IsEnabled;
        _theme.Items.AddRange(Theme.Choices.Select(c => c.Label).ToArray());
        _theme.SelectedIndex = Math.Max(0, Array.FindIndex(Theme.Choices, c => c.Value == settings.Theme));
        _theme.SelectedIndexChanged += (_, _) => ThemePicked?.Invoke(SelectedTheme);
        // Wired after the initial values are set, so opening the window doesn't count as a change.
        foreach (var box in new[] { _sound, _preview, _freeze, _pauseMedia, _saveMp4, _hdrJxr, _hdrPng })
            box.CheckedChanged += (_, _) => ApplyChange();
        _gifQuality.SelectedIndexChanged += (_, _) => ApplyChange();
        _controller.SelectedIndexChanged += (_, _) => ApplyChange();
        _startup.CheckedChanged += (_, _) =>
        {
            try { StartupRegistration.Set(_startup.Checked); }
            catch (Exception ex) { Log.Write($"Could not change startup setting: {ex.Message}"); }
        };
        foreach (var box in new[] { _fullScreen, _region, _edit, _gif, _gifEdit })
        {
            box.Enter += (_, _) => RecordingShortcut?.Invoke(true);
            box.Leave += (_, _) => RecordingShortcut?.Invoke(false);
            box.Done += (_, _) =>
            {
                ApplyChange();
                ActiveControl = _closeButton;
            };
        }

        var grid = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };
        grid.Controls.Add(Header());

        // Tabs: a row of toggle buttons rather than a TabControl, which doesn't follow dark mode.
        var shortcuts = Page();
        AddRow(shortcuts, "Full screen", _fullScreen);
        AddRow(shortcuts, "Pick a region", _region);
        AddRow(shortcuts, "Capture and edit", _edit);
        AddRow(shortcuts, "Record a GIF", _gif);
        AddRow(shortcuts, "Record and edit a GIF", _gifEdit);
        AddRow(shortcuts, "Controller: full screen", _controller);
        AddWide(shortcuts, Hint("Click a box, then press the keys you want. ClearShot works in games too. Capture and edit lets you draw arrows, text and numbered steps, hide details and pin the picture on screen before copying. The controller combination (hold both buttons) works in games too, with Xbox controllers and PlayStation ones through DS4Windows or Steam; the controller gives a short buzz when the shot is taken."));

        var saving = Page();
        var browse = new Button { Text = "Change…", AutoSize = true };
        browse.Click += (_, _) => ChooseFolder();
        var open = new Button { Text = "Open folder", AutoSize = true };
        open.Click += (_, _) => TrayApp.OpenFolder(_folder.Text);
        AddRow(saving, "Save screenshots to", _folder, browse, open);
        AddWide(saving, Hint("Every screenshot is saved here as a PNG and copied to your clipboard, ready to paste."));

        var recording = Page();
        AddRow(recording, "GIF quality", _gifQuality);
        AddWide(recording, Hint("Standard: up to 960 px wide at 15 fps, small files, ideal for Discord. High: up to 1920 px at 30 fps, bigger files. For game and video footage, the MP4 option looks far better than any GIF."));
        AddWide(recording, _saveMp4);

        var hdr = Page();
        AddWide(hdr, _hdrJxr);
        AddWide(hdr, _hdrPng);
        AddWide(hdr, Hint("When your screen is in HDR, also save true HDR copies next to the normal PNG. The normal PNG is still what gets copied, because most apps can't show HDR."));
        AddWide(hdr, HdrExample());

        var options = Page();
        AddWide(options, _sound);
        AddWide(options, _preview);
        AddWide(options, _freeze);
        AddWide(options, _pauseMedia);
        AddWide(options, _startup);
        AddRow(options, "Appearance", _theme);

        var tabs = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 10, 0, 6) };
        var pages = _pages = new Panel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, Dock = DockStyle.Fill };
        (string Name, TableLayoutPanel Page)[] all = [("Shortcuts", shortcuts), ("Saving", saving), ("GIFs", recording), ("HDR", hdr), ("Options", options)];
        for (int i = 0; i < all.Length; i++)
        {
            var (name, page) = all[i];
            int index = i;
            page.Visible = false;
            pages.Controls.Add(page);
            var tab = new RadioButton
            {
                Text = name,
                Appearance = Appearance.Button,
                AutoSize = true,
                MinimumSize = new Size(96, 30),
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(0, 0, 4, 0),
            };
            tab.CheckedChanged += (_, _) =>
            {
                page.Visible = tab.Checked;
                if (tab.Checked) _lastTab = index;
            };
            tabs.Controls.Add(tab);
        }
        ((RadioButton)tabs.Controls[Math.Clamp(_lastTab, 0, all.Length - 1)]).Checked = true;
        grid.Controls.Add(tabs);
        grid.Controls.Add(pages);

        var about = new Label
        {
            Text = $"Version {AppInfo.Version}. Free and open source. No account, no uploads: your screenshots never leave this PC.",
            AutoSize = true,
            MaximumSize = new Size(640, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(3, 18, 3, 6),
        };
        grid.Controls.Add(about);

        var close = _closeButton;
        close.Click += (_, _) => Close();
        // One row: the links on the left, Close on the right. Every cell sizes to its content,
        // so nothing can be pushed out of view at any display scaling.
        var footer = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var links = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
        var help = new LinkLabel { Text = "How to use", AutoSize = true, Margin = new Padding(3, 0, 16, 0) };
        help.LinkClicked += (_, _) => HelpForm.ShowFor(_settings, this);
        links.Controls.Add(help);
        if (AppInfo.ActiveDonations.Count > 0)
        {
            var donate = new LinkLabel { Text = "Buy me a beer", AutoSize = true, Margin = new Padding(3, 0, 3, 0) };
            donate.LinkClicked += (_, _) => DonateForm.ShowFor(AppInfo.ActiveDonations, this);
            links.Controls.Add(donate);
        }
        footer.Controls.Add(links, 0, 0);
        footer.Controls.Add(close, 1, 0);
        grid.Controls.Add(footer);

        CancelButton = close;
        Controls.Add(grid);
        Theme.Style(this);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Start with focus on a button, not a shortcut box, or the window would open already recording.
        ActiveControl = _closeButton;
    }

    private string SelectedTheme => Theme.Choices[Math.Max(0, _theme.SelectedIndex)].Value;

    public Draft CaptureDraft() => new(new Settings
    {
        SaveFolder = _folder.Text,
        FullScreenHotkey = _fullScreen.Value.ToString(),
        RegionHotkey = _region.Value.ToString(),
        GifHotkey = _gif.Value.ToString(),
        EditHotkey = _edit.Value.ToString(),
        GifEditHotkey = _gifEdit.Value.ToString(),
        ControllerShortcut = ControllerShortcut.Choices[Math.Max(0, _controller.SelectedIndex)].Value,
        PlaySound = _sound.Checked,
        ShowPreview = _preview.Checked,
        PauseMediaWhileSelecting = _pauseMedia.Checked,
        FreezeWhileSelecting = _freeze.Checked,
        GifQuality = _gifQuality.SelectedIndex == 1 ? "High" : "Standard",
        SaveMp4 = _saveMp4.Checked,
        SaveHdrJxr = _hdrJxr.Checked,
        SaveHdrPng = _hdrPng.Checked,
        Theme = SelectedTheme,
    }, _startup.Checked);

    private Control Header()
    {
        var panel = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var logo = new PictureBox
        {
            Image = TrayApp.LoadAppIcon(48).ToBitmap(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(40, 40),
            Margin = new Padding(0, 2, 12, 0),
        };
        var title = new Label { Text = AppInfo.Name, AutoSize = true, Font = new Font("Segoe UI Semibold", 15f), Margin = new Padding(0, 0, 0, 0) };
        var status = new Label
        {
            Text = "Changes save as you make them. Close this window and ClearShot keeps running in the tray.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(2, 0, 0, 0),
        };
        var text = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        text.Controls.Add(title);
        text.Controls.Add(status);
        panel.Controls.Add(logo, 0, 0);
        panel.Controls.Add(text, 1, 0);
        return panel;
    }

    /// <summary>
    /// Side by side: what most screenshot tools save when Windows HDR is on (grey and washed out), and what
    /// ClearShot saves (what you actually saw). Every screenshot and GIF gets this, no option needed.
    /// </summary>
    private static Control HdrExample()
    {
        var panel = new TableLayoutPanel { ColumnCount = 2, RowCount = 3, AutoSize = true, Margin = new Padding(0, 14, 0, 0) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title = new Label
        {
            Text = "Why HDR screenshots usually look wrong",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10.5f),
            Margin = new Padding(3, 0, 3, 6),
        };
        panel.Controls.Add(title, 0, 0);
        panel.SetColumnSpan(title, 2);
        (string Resource, string Caption)[] pictures =
        [
            ("hdr-other-apps.jpg", "Most screenshot tools with HDR on: grey and washed out"),
            ("hdr-clearshot.jpg", "ClearShot: the colours you actually saw"),
        ];
        for (int i = 0; i < pictures.Length; i++)
        {
            using var stream = typeof(SettingsForm).Assembly.GetManifestResourceStream(pictures[i].Resource);
            var picture = new PictureBox
            {
                Image = stream is null ? null : Image.FromStream(stream),
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(308, 173),
                Margin = new Padding(3, 0, i == 0 ? 12 : 3, 4),
            };
            panel.Controls.Add(picture, i, 1);
            panel.Controls.Add(new Label { Text = pictures[i].Caption, AutoSize = true, MaximumSize = new Size(308, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 0, 3, 0) }, i, 2);
        }
        return panel;
    }

    private static Label SectionTitle(string text, bool first = false) =>
        new() { Text = text, AutoSize = true, Font = new Font("Segoe UI Semibold", 10.5f), Margin = new Padding(3, first ? 4 : 16, 3, 6) };

    // The tab that was open last, so rebuilding the window (for a new appearance) keeps you where you were.
    private static int _lastTab;
    private Panel? _pages;

    /// <summary>Makes the tab area as big as the biggest tab, so the window keeps one size whichever tab is open.</summary>
    private void FitTallestTab()
    {
        if (_pages is null) return;
        _pages.MinimumSize = Size.Empty;
        // Lay each tab out for real and keep the biggest. Predicted sizes can differ from real ones where text wraps.
        var shown = _pages.Controls.Cast<Control>().Select(c => c.Visible).ToArray();
        var biggest = Size.Empty;
        foreach (Control page in _pages.Controls)
        {
            foreach (Control other in _pages.Controls) other.Visible = ReferenceEquals(other, page);
            _pages.PerformLayout();
            PerformLayout();
            biggest = new Size(Math.Max(biggest.Width, page.Width), Math.Max(biggest.Height, page.Height));
        }
        for (int i = 0; i < shown.Length; i++) _pages.Controls[i].Visible = shown[i];
        _pages.MinimumSize = biggest;
    }

    protected override void OnLoad(EventArgs e)
    {
        // After the window has been scaled for the display: measuring before gives unscaled sizes.
        base.OnLoad(e);
        FitTallestTab();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        // Moving to a monitor with different scaling resizes everything: measure again.
        FitTallestTab();
    }

    /// <summary>One tab's contents: label column, a fixed-width field column, then room for buttons.</summary>
    private static TableLayoutPanel Page()
    {
        var page = new TableLayoutPanel { ColumnCount = 4, AutoSize = true, Dock = DockStyle.Top, Margin = Padding.Empty, MinimumSize = new Size(640, 0) };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        page.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        page.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return page;
    }

    private static Label Hint(string text) =>
        new() { Text = text, AutoSize = true, MaximumSize = new Size(640, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 2, 3, 4) };

    private static Hotkey Parse(string text, string fallback) =>
        Hotkey.TryParse(text, out var hk) ? hk : Hotkey.TryParse(fallback, out var fb) ? fb : default;

    /// <returns>The row's controls (to show or hide them together).</returns>
    private static Control[] AddRow(TableLayoutPanel grid, string label, Control field, params Control[] extras)
    {
        if (field is ComboBox) field.Anchor = AnchorStyles.Left;
        int row = grid.RowCount++;
        var name = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 12, 3) };
        grid.Controls.Add(name, 0, row);
        grid.Controls.Add(field, 1, row);
        for (int i = 0; i < extras.Length; i++) grid.Controls.Add(extras[i], 2 + i, row);
        return [name, field, .. extras];
    }

    private static Control AddWide(TableLayoutPanel grid, Control control)
    {
        int row = grid.RowCount++;
        grid.Controls.Add(control, 0, row);
        grid.SetColumnSpan(control, 4);
        return control;
    }

    private void ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog { SelectedPath = _folder.Text, ShowNewFolderButton = true, Description = "Where should screenshots be saved?" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _folder.Text = dialog.SelectedPath;
        ApplyChange();
    }

    /// <summary>Checks and stores what's in the window, then tells ClearShot to save it. Invalid input is undone.</summary>
    internal void ApplyChange()
    {
        var shortcuts = new List<Hotkey> { _fullScreen.Value, _region.Value, _gif.Value, _edit.Value, _gifEdit.Value };
        if (shortcuts.Distinct().Count() != shortcuts.Count)
        {
            MessageBox.Show(this, "Each shortcut needs to be different.", AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            _fullScreen.Value = Parse(_settings.FullScreenHotkey, "Alt+C");
            _region.Value = Parse(_settings.RegionHotkey, "Alt+Shift+C");
            _gif.Value = Parse(_settings.GifHotkey, "Alt+G");
            _edit.Value = Parse(_settings.EditHotkey, "Alt+Shift+E");
            _gifEdit.Value = Parse(_settings.GifEditHotkey, "Alt+Shift+G");
            return;
        }
        try
        {
            Directory.CreateDirectory(_folder.Text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"ClearShot can't save to that folder.\n\n{ex.Message}", AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _folder.Text = _settings.SaveFolder;
            return;
        }

        _settings.SaveFolder = _folder.Text;
        _settings.FullScreenHotkey = _fullScreen.Value.ToString();
        _settings.RegionHotkey = _region.Value.ToString();
        _settings.GifHotkey = _gif.Value.ToString();
        _settings.EditHotkey = _edit.Value.ToString();
        _settings.GifEditHotkey = _gifEdit.Value.ToString();
        _settings.ControllerShortcut = ControllerShortcut.Choices[Math.Max(0, _controller.SelectedIndex)].Value;
        _settings.PlaySound = _sound.Checked;
        _settings.ShowPreview = _preview.Checked;
        _settings.PauseMediaWhileSelecting = _pauseMedia.Checked;
        _settings.FreezeWhileSelecting = _freeze.Checked;
        _settings.GifQuality = _gifQuality.SelectedIndex == 1 ? "High" : "Standard";
        _settings.SaveMp4 = _saveMp4.Checked;
        _settings.SaveHdrJxr = _hdrJxr.Checked;
        _settings.SaveHdrPng = _hdrPng.Checked;
        SettingsChanged?.Invoke();
    }
}
