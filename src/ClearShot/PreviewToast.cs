using System.Drawing.Drawing2D;

namespace ClearShot;

/// <summary>A small thumbnail in the corner that never takes focus, so it won't pull you out of a game.</summary>
internal sealed class PreviewToast : Form
{
    private const int WsExToolWindow = 0x80, WsExNoActivate = 0x08000000, WsExTopmost = 0x8;
    private readonly Bitmap _thumb;
    private readonly string _path;
    private readonly string _caption;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2600 };

    public PreviewToast(Bitmap image, string path, Rectangle monitorBounds, bool hdrCopy, string? caption = null)
    {
        _path = path;
        _caption = caption ?? $"Copied and saved  ·  {image.Width} × {image.Height}" + (hdrCopy ? "  ·  HDR" : "");
        float scale = Dpi.ScaleFor(monitorBounds);
        int maxW = (int)(240 * scale), maxH = (int)(150 * scale), pad = (int)(8 * scale), captionH = (int)(26 * scale);
        float fit = Math.Min((float)maxW / image.Width, (float)maxH / image.Height);
        var thumbSize = new Size(Math.Max(1, (int)(image.Width * fit)), Math.Max(1, (int)(image.Height * fit)));
        _thumb = new Bitmap(thumbSize.Width, thumbSize.Height);
        using (var g = Graphics.FromImage(_thumb))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(image, new Rectangle(Point.Empty, thumbSize));
        }

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.FromArgb(15, 23, 42);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 12f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        int captionW = TextRenderer.MeasureText(_caption, Font).Width + pad * 2;
        var size = new Size(Math.Max(Math.Max(thumbSize.Width, (int)(200 * scale)), captionW) + pad * 2, thumbSize.Height + pad * 2 + captionH);
        var work = Screen.FromRectangle(monitorBounds).WorkingArea;
        Bounds = new Rectangle(work.Right - size.Width - pad * 2, work.Bottom - size.Height - pad * 2, size.Width, size.Height);

        _timer.Tick += (_, _) => Close();
        Click += (_, _) => OpenInExplorer();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow | WsExNoActivate | WsExTopmost;
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _timer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int pad = (Height - _thumb.Height - TextRenderer.MeasureText(_caption, Font).Height) / 3;
        pad = Math.Max(pad, 4);
        g.DrawImageUnscaled(_thumb, (Width - _thumb.Width) / 2, pad);
        var captionBox = new Rectangle(0, pad + _thumb.Height, Width, Height - pad - _thumb.Height);
        TextRenderer.DrawText(g, _caption, Font, captionBox, Color.FromArgb(203, 213, 225),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void OpenInExplorer()
    {
        if (File.Exists(_path))
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_path}\"");
        Close();
    }


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _thumb.Dispose();
        }
        base.Dispose(disposing);
    }
}
