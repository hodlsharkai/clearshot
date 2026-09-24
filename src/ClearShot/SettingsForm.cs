namespace ClearShot;

internal sealed class SettingsForm : Form
{
    private readonly Settings _settings;
    private readonly TextBox _folder = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly HotkeyBox _fullScreen = new() { Dock = DockStyle.Fill };
    private readonly HotkeyBox _region = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _sound = new() { Text = "Play a shutter sound", AutoSize = true };
    private readonly CheckBox _preview = new() { Text = "Show a small preview after each capture", AutoSize = true };
    private readonly CheckBox _startup = new() { Text = "Start ClearShot with Windows", AutoSize = true };

    public SettingsForm(Settings settings)
    {
        _settings = settings;
        Text = "ClearShot settings";
        Icon = TrayApp.LoadAppIcon(32);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Font = new Font("Segoe UI", 9f);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        _folder.Text = settings.SaveFolder;
        _fullScreen.Value = Parse(settings.FullScreenHotkey, "PrintScreen");
        _region.Value = Parse(settings.RegionHotkey, "Ctrl+PrintScreen");
        _sound.Checked = settings.PlaySound;
        _preview.Checked = settings.ShowPreview;
        _startup.Checked = StartupRegistration.IsEnabled;

        var browse = new Button { Text = "Change…", AutoSize = true };
        browse.Click += (_, _) => ChooseFolder();
        var open = new Button { Text = "Open", AutoSize = true };
        open.Click += (_, _) => TrayApp.OpenFolder(_folder.Text);

        var grid = new TableLayoutPanel { ColumnCount = 4, AutoSize = true, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddRow(grid, "Save screenshots to", _folder, browse, open);
        AddRow(grid, "Full screen shortcut", _fullScreen);
        AddRow(grid, "Region shortcut", _region);
        AddWide(grid, new Label { Text = "Click a shortcut box, then press the keys you want.", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 0, 3, 10) });
        AddWide(grid, _sound);
        AddWide(grid, _preview);
        AddWide(grid, _startup);

        var about = new Label
        {
            Text = $"ClearShot {AppInfo.Version}. Free and open source. No account, no uploads: your screenshots never leave this PC.",
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(3, 14, 3, 6),
        };
        AddWide(grid, about);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
        var save = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        save.Click += (_, e) => { if (!Commit()) DialogResult = DialogResult.None; };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);
        if (!string.IsNullOrEmpty(AppInfo.DonateUrl))
        {
            var donate = new LinkLabel { Text = "Support ClearShot", AutoSize = true, Margin = new Padding(3, 8, 24, 3) };
            donate.LinkClicked += (_, _) => AppInfo.OpenUrl(AppInfo.DonateUrl);
            buttons.Controls.Add(donate);
        }
        AddWide(grid, buttons);

        AcceptButton = save;
        CancelButton = cancel;
        Controls.Add(grid);
    }

    private static Hotkey Parse(string text, string fallback) =>
        Hotkey.TryParse(text, out var hk) ? hk : Hotkey.TryParse(fallback, out var fb) ? fb : default;

    private static void AddRow(TableLayoutPanel grid, string label, Control field, params Control[] extras)
    {
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
            MessageBox.Show(this, "The two shortcuts need to be different.", "ClearShot", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        try
        {
            Directory.CreateDirectory(_folder.Text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"ClearShot can't save to that folder.\n\n{ex.Message}", "ClearShot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        _settings.SaveFolder = _folder.Text;
        _settings.FullScreenHotkey = _fullScreen.Value.ToString();
        _settings.RegionHotkey = _region.Value.ToString();
        _settings.PlaySound = _sound.Checked;
        _settings.ShowPreview = _preview.Checked;
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
