using QRCoder;

namespace ClearShot;

/// <summary>"Buy me a beer": each crypto address with a Copy button and a QR code for phone wallets. Works offline.</summary>
internal sealed class DonateForm : Form
{
    private static DonateForm? _open;

    public static void ShowFor(IReadOnlyList<DonationAddress> addresses, IWin32Window? owner = null)
    {
        if (_open is not null)
        {
            _open.Activate();
            return;
        }
        _open = new DonateForm(addresses);
        _open.FormClosed += (_, _) => { _open.Dispose(); _open = null; };
        _open.Show(owner);
    }

    internal DonateForm(IReadOnlyList<DonationAddress> addresses)
    {
        Text = "Buy me a beer";
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
        Padding = new Padding(20, 16, 20, 16);

        var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };
        layout.Controls.Add(new Label
        {
            Text = "ClearShot is free, with no ads and no accounts. If it's useful to you, a tip keeps it going. Thank you!",
            AutoSize = true,
            MaximumSize = new Size(480, 0),
            Margin = new Padding(3, 0, 3, 12),
        });

        // A row of toggle buttons rather than a TabControl, which doesn't follow dark mode.
        var picker = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
        var pages = new Panel { AutoSize = true, Margin = Padding.Empty };
        Control? first = null;
        foreach (var donation in addresses)
        {
            var page = Page(donation);
            page.Visible = false;
            pages.Controls.Add(page);
            var choice = new RadioButton { Text = donation.Network, Appearance = Appearance.Button, AutoSize = true, MinimumSize = new Size(90, 0), TextAlign = ContentAlignment.MiddleCenter };
            choice.CheckedChanged += (_, _) => page.Visible = choice.Checked;
            picker.Controls.Add(choice);
            if (first is null) { first = choice; choice.Checked = true; }
        }
        layout.Controls.Add(picker);
        layout.Controls.Add(pages);

        layout.Controls.Add(new Label
        {
            Text = "Only send each coin to its matching address. Coins sent on the wrong network can be lost.",
            AutoSize = true,
            MaximumSize = new Size(480, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(3, 10, 3, 0),
        });

        var close = new Button { Text = "Close", AutoSize = true, MinimumSize = new Size(88, 0), Anchor = AnchorStyles.Right, DialogResult = DialogResult.Cancel, Margin = new Padding(3, 12, 3, 0) };
        close.Click += (_, _) => Close();
        layout.Controls.Add(close);
        CancelButton = close;
        Controls.Add(layout);
        Theme.Style(this);
    }

    private static Control Page(DonationAddress donation)
    {
        var stack = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Width = 500, Margin = Padding.Empty };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var qr = new PictureBox
        {
            Image = QrImage(donation.Address),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(180, 180),
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 0, 0, 10),
        };
        stack.Controls.Add(qr, 0, 0);
        stack.SetColumnSpan(qr, 2);

        var address = new TextBox
        {
            Text = donation.Address,
            ReadOnly = true,
            Width = 410,
            Anchor = AnchorStyles.Left,
            Font = new Font("Consolas", 9.5f),
        };
        var copy = new Button { Text = "Copy", AutoSize = true, MinimumSize = new Size(72, 0) };
        copy.Click += (_, _) =>
        {
            Clipboard.SetText(donation.Address);
            copy.Text = "Copied";
        };
        stack.Controls.Add(address, 0, 1);
        stack.Controls.Add(copy, 1, 1);

        var accepts = new Label { Text = donation.Accepts, AutoSize = true, MaximumSize = new Size(460, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 8, 3, 0) };
        stack.Controls.Add(accepts, 0, 2);
        stack.SetColumnSpan(accepts, 2);

        return stack;
    }

    private static Image QrImage(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(8);
        using var ms = new MemoryStream(png);
        return new Bitmap(Image.FromStream(ms));
    }
}
