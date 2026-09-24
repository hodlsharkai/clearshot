namespace ClearShot;

/// <summary>ClearShot's main window: shortcuts, save folder and options.</summary>
internal sealed class SettingsForm : Form
{
    private readonly Settings _settings;
    private readonly TextBox _folder = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly HotkeyBox _fullScreen = new() { Dock = DockStyle.Fill };
    private readonly HotkeyBox _region = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _sound = new() { Text = "Play a shutter sound", AutoSize = true };
    private readonly CheckBox _preview = new() { Text = "Show a small preview after each capture", AutoSize = true };
    private readonly CheckBox _pauseMedia = new() { Text = "Pause videos and music while picking a region", AutoSize = true };
    private readonly CheckBox _hdrJxr = new() { Text = "Save a .jxr copy (opens in HDR in Windows Photos)", AutoSize = true };
    private readonly CheckBox _hdrPng = new() { Text = "Save an HDR PNG copy (shows in HDR in Chrome and Edge)", AutoSize = true };
    private readonly CheckBox _startup = new() { Text = "Start ClearShot with Windows", AutoSize = true };
    private readonly ComboBox _theme = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly Button _closeButton = new() { Text = "Close", AutoSize = true, MinimumSize = new Size(88, 0), DialogResult = DialogResult.Cancel };

    /// <summary>True while a shortcut box is waiting for keys, so the real shortcuts should be switched off.</summary>
    public event Action<bool>? RecordingShortcut;

    public SettingsForm(Settings settings)
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

        _folder.Text = settings.SaveFolder;
        _fullScreen.Value = Parse(settings.FullScreenHotkey, "Alt+C");
        _region.Value = Parse(settings.RegionHotkey, "Alt+Shift+C");
        _sound.Checked = settings.PlaySound;
        _preview.Checked = settings.ShowPreview;
        _pauseMedia.Checked = settings.PauseMediaWhileSelecting;
        _hdrJxr.Checked = settings.SaveHdrJxr;
        _hdrPng.Checked = settings.SaveHdrPng;
        _startup.Checked = StartupRegistration.IsEnabled;
        _theme.Items.AddRange(Theme.Choices.Select(c => c.Label).ToArray());
        _theme.SelectedIndex = Math.Max(0, Array.FindIndex(Theme.Choices, c => c.Value == settings.Theme));
        foreach (var box in new[] { _fullScreen, _region })
        {
            box.Enter += (_, _) => RecordingShortcut?.Invoke(true);
            box.Leave += (_, _) => RecordingShortcut?.Invoke(false);
            box.Done += (_, _) => ActiveControl = _closeButton;
        }

        var grid = new TableLayoutPanel { ColumnCount = 4, AutoSize = true, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddWide(grid, Header());

        AddWide(grid, SectionTitle("Shortcuts"));
        AddRow(grid, "Full screen", _fullScreen);
        AddRow(grid, "Pick a region", _region);
        AddWide(grid, Hint("Click a box, then press the keys you want. ClearShot works in games too."));

        AddWide(grid, SectionTitle("Saving"));
        var browse = new Button { Text = "Change…", AutoSize = true };
        browse.Click += (_, _) => ChooseFolder();
        var open = new Button { Text = "Open folder", AutoSize = true };
        open.Click += (_, _) => TrayApp.OpenFolder(_folder.Text);
        AddRow(grid, "Save screenshots to", _folder, browse, open);
        AddWide(grid, Hint("Every screenshot is saved here as a PNG and copied to your clipboard, ready to paste."));

        AddWide(grid, SectionTitle("HDR mode"));
        AddWide(grid, _hdrJxr);
        AddWide(grid, _hdrPng);
        AddWide(grid, Hint("When your screen is in HDR, also save true HDR copies next to the normal PNG. The normal PNG is still what gets copied, because most apps can't show HDR."));

        AddWide(grid, SectionTitle("Options"));
        AddWide(grid, _sound);
        AddWide(grid, _preview);
        AddWide(grid, _pauseMedia);
        AddWide(grid, _startup);
        AddRow(grid, "Appearance", _theme);

        var about = new Label
        {
            Text = $"Version {AppInfo.Version}. Free and open source. No account, no uploads: your screenshots never leave this PC.",
            AutoSize = true,
            MaximumSize = new Size(640, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(3, 18, 3, 6),
        };
        AddWide(grid, about);

        var save = new Button { Text = "Save", AutoSize = true, MinimumSize = new Size(88, 0), Anchor = AnchorStyles.Right };
        var close = _closeButton;
        save.Click += (_, _) =>
        {
            if (!Commit()) return;
            DialogResult = DialogResult.OK;
            Close();
        };
        close.Click += (_, _) => Close();
        // One row: "Buy me a beer" on the left, Save and Close on the right. Every cell sizes to its content,
        // so nothing can be pushed out of view at any display scaling.
        var footer = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (AppInfo.ActiveDonations.Count > 0)
        {
            var donate = new LinkLabel { Text = "Buy me a beer", AutoSize = true, Anchor = AnchorStyles.Left };
            donate.LinkClicked += (_, _) => DonateForm.ShowFor(AppInfo.ActiveDonations, this);
            footer.Controls.Add(donate, 0, 0);
        }
        footer.Controls.Add(save, 1, 0);
        footer.Controls.Add(close, 2, 0);
        AddWide(grid, footer);

        AcceptButton = save;
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
            Text = "Running in your system tray. Close this window and it keeps working.",
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

    private static Label SectionTitle(string text) =>
        new() { Text = text, AutoSize = true, Font = new Font("Segoe UI Semibold", 10.5f), Margin = new Padding(3, 16, 3, 6) };

    private static Label Hint(string text) =>
        new() { Text = text, AutoSize = true, MaximumSize = new Size(640, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 2, 3, 4) };

    private static Hotkey Parse(string text, string fallback) =>
        Hotkey.TryParse(text, out var hk) ? hk : Hotkey.TryParse(fallback, out var fb) ? fb : default;

    private static void AddRow(TableLayoutPanel grid, string label, Control field, params Control[] extras)
    {
        if (field is ComboBox) field.Anchor = AnchorStyles.Left;
        int row = grid.RowCount++;
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 12, 3) }, 0, row);
        grid.Controls.Add(field, 1, row);
        for (int i = 0; i < extras.Length; i++) grid.Controls.Add(extras[i], 2 + i, row);
    }

    private static void AddWide(TableLayoutPanel grid, Control control)
    {
        int row = grid.RowCount++;
        grid.Controls.Add(control, 0, row);
        grid.SetColumnSpan(control, 4);
    }

    private void ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog { SelectedPath = _folder.Text, ShowNewFolderButton = true, Description = "Where should screenshots be saved?" };
        if (dialog.ShowDialog(this) == DialogResult.OK) _folder.Text = dialog.SelectedPath;
    }

    private bool Commit()
    {
        if (_fullScreen.Value == _region.Value)
        {
            MessageBox.Show(this, "The two shortcuts need to be different.", AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        try
        {
            Directory.CreateDirectory(_folder.Text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"ClearShot can't save to that folder.\n\n{ex.Message}", AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        _settings.SaveFolder = _folder.Text;
        _settings.FullScreenHotkey = _fullScreen.Value.ToString();
        _settings.RegionHotkey = _region.Value.ToString();
        _settings.PlaySound = _sound.Checked;
        _settings.ShowPreview = _preview.Checked;
        _settings.PauseMediaWhileSelecting = _pauseMedia.Checked;
        _settings.SaveHdrJxr = _hdrJxr.Checked;
        _settings.SaveHdrPng = _hdrPng.Checked;
        _settings.Theme = Theme.Choices[Math.Max(0, _theme.SelectedIndex)].Value;
        try
        {
            StartupRegistration.Set(_startup.Checked);
        }
        catch (Exception ex)
        {
            Log.Write($"Could not change startup setting: {ex.Message}");
        }
        return true;
    }
}
