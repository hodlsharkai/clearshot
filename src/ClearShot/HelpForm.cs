namespace ClearShot;

/// <summary>"How to use": the shortcuts as they're currently set, and every editor tool and key. Works offline.</summary>
internal sealed class HelpForm : Form
{
    private static HelpForm? _open;

    public static void ShowFor(Settings settings, IWin32Window? owner = null)
    {
        if (_open is not null)
        {
            _open.Activate();
            return;
        }
        _open = new HelpForm(settings);
        _open.FormClosed += (_, _) => { _open.Dispose(); _open = null; };
        _open.Show(owner);
    }

    internal HelpForm(Settings settings)
    {
        Text = "How to use ClearShot";
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
        Padding = new Padding(20, 12, 20, 16);

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        Section(grid, "Shortcuts");
        Row(grid, Key(settings.FullScreenHotkey), "Capture the whole monitor your mouse is on.");
        Row(grid, Key(settings.RegionHotkey), "Drag a box around the part you want.");
        Row(grid, Key(settings.EditHotkey), "Drag a box, then draw on it before copying or saving.");
        Row(grid, Key(settings.GifHotkey), "Record a GIF: drag a box, press the shortcut again to stop. Up to 15 seconds.");
        Note(grid, "Every capture is copied, ready to paste, and saved as a PNG. Change the shortcuts in the ClearShot window.");

        Section(grid, "Drawing tools (capture and edit)");
        Row(grid, "V", "Select: click any drawing to move it, recolour it, resize it (scroll) or delete it (Delete). Drag the squares on an arrow's ends or a box's corners to reshape it.");
        Row(grid, "P", "Pen");
        Row(grid, "L  /  A", "Line  /  Arrow");
        Row(grid, "R", "Rectangle");
        Row(grid, "H", "Highlighter");
        Row(grid, "T", "Text. Pick a font from the Aa button, drag the corner square to make it any size, click finished text to edit it.");
        Row(grid, "N", "Numbered steps: click to drop 1, 2, 3...");
        Row(grid, "E", "Emoji: pick one from the smiley button, then click to place it. Scroll to resize.");
        Row(grid, "B", "Pixelate: drag over names, emails or addresses to hide them.");
        Row(grid, "X", "Erase: rub out drawings and pixelation, to trim a pixelated area to exactly the shape you want hidden.");
        Row(grid, "Scroll", "Thicker or thinner lines, bigger or smaller text.");
        Row(grid, "Ctrl + Z", "Undo, one step at a time.");
        Note(grid, "Each tool remembers its own colour. Changing the colour also recolours what you just drew or have selected. Drag the dotted edges to resize the area.");

        Section(grid, "When you're done");
        Row(grid, "Enter", "Copy and save.");
        Row(grid, "Ctrl + C", "Copy only.");
        Row(grid, "Ctrl + S", "Save only.");
        Row(grid, "Pin", "Float the picture on top of your windows. Drag to move, scroll to resize, double-click to close.");
        Row(grid, "Esc", "Close without saving (the first Esc just finishes text or deselects). Right-click also closes.");

        var close = new Button { Text = "Close", AutoSize = true, MinimumSize = new Size(88, 0), Anchor = AnchorStyles.Right, Margin = new Padding(3, 14, 3, 0) };
        close.Click += (_, _) => Close();
        int row = grid.RowCount++;
        grid.Controls.Add(close, 0, row);
        grid.SetColumnSpan(close, 2);
        CancelButton = close;
        AcceptButton = close;

        Controls.Add(grid);
        Theme.Style(this);
    }

    private static string Key(string text) => Hotkey.TryParse(text, out var hk) ? hk.DisplayText : text;

    private static void Section(TableLayoutPanel grid, string title)
    {
        var label = new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI Semibold", 10.5f), Margin = new Padding(3, 12, 3, 6) };
        int row = grid.RowCount++;
        grid.Controls.Add(label, 0, row);
        grid.SetColumnSpan(label, 2);
    }

    private static void Row(TableLayoutPanel grid, string key, string text)
    {
        int row = grid.RowCount++;
        grid.Controls.Add(new Label { Text = key, AutoSize = true, Font = new Font("Segoe UI Semibold", 9f), Margin = new Padding(3, 3, 16, 3) }, 0, row);
        grid.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(440, 0), Margin = new Padding(3, 3, 3, 3) }, 1, row);
    }

    private static void Note(TableLayoutPanel grid, string text)
    {
        var label = new Label { Text = text, AutoSize = true, MaximumSize = new Size(540, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 4, 3, 2) };
        int row = grid.RowCount++;
        grid.Controls.Add(label, 0, row);
        grid.SetColumnSpan(label, 2);
    }
}
