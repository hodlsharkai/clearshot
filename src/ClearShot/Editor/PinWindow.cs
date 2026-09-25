using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ClearShot.Editor;

/// <summary>
/// A screenshot floating on top of everything, like a sticky note. Drag to move, scroll to resize,
/// double-click or Esc to close; right-click for Copy, Save and Close.
/// </summary>
internal sealed class PinWindow : Form
{
    private const int WsExToolWindow = 0x80, WsExTopmost = 0x8;
    private readonly Bitmap _image;
    private readonly Func<Bitmap, string?> _save;
    private float _zoom = 1f;

    /// <param name="image">The picture; the window owns it from now on.</param>
    /// <param name="save">Saves the picture and returns where, or null if it failed.</param>
    public PinWindow(Bitmap image, Point screenLocation, Func<Bitmap, string?> save)
    {
        _image = image;
        _save = save;
        Text = "ClearShot pin";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        Cursor = Cursors.SizeAll;
        Bounds = new Rectangle(screenLocation, image.Size + new Size(2, 2));

        var menu = new ContextMenuStrip();
        menu.Items.Add("Copy", null, (_, _) => ClipboardOutput.Copy(_image, ClipboardOutput.EncodePng(_image)));
        menu.Items.Add("Save", null, (_, _) => _save(_image));
        menu.Items.Add("Actual size", null, (_, _) => ZoomTo(1f, new Point(Width / 2, Height / 2)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Close", null, (_, _) => Close());
        ContextMenuStrip = menu;
    }

    public float Zoom => _zoom;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow | WsExTopmost;
            return cp;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = _zoom == 1f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_image, new Rectangle(1, 1, Width - 2, Height - 2));
        using var edge = new Pen(ToolBar.Accent);
        g.DrawRectangle(edge, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || e.Clicks > 1) return;
        // Let Windows move the window, as if the whole picture were a title bar.
        ReleaseCapture();
        SendMessage(Handle, 0xA1 /* WM_NCLBUTTONDOWN */, 2 /* HTCAPTION */, 0);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left) Close();
    }

    protected override void WndProc(ref Message m)
    {
        // The drag above hands the mouse to Windows, which reports the double-click on the "title bar".
        if (m.Msg == 0xA3 /* WM_NCLBUTTONDBLCLK */) { Close(); return; }
        base.WndProc(ref m);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ZoomTo(_zoom * (e.Delta > 0 ? 1.1f : 1 / 1.1f), e.Location);
    }

    /// <summary>Resizes around <paramref name="anchor"/> (a point in the window), so the spot under the mouse stays put.</summary>
    internal void ZoomTo(float zoom, Point anchor)
    {
        zoom = Math.Clamp(zoom, 0.1f, 4f);
        float fx = Width > 0 ? anchor.X / (float)Width : 0.5f, fy = Height > 0 ? anchor.Y / (float)Height : 0.5f;
        var size = new Size(Math.Max(8, (int)Math.Round(_image.Width * zoom)) + 2, Math.Max(8, (int)Math.Round(_image.Height * zoom)) + 2);
        var screenAnchor = PointToScreen(anchor);
        _zoom = zoom;
        Bounds = new Rectangle((int)Math.Round(screenAnchor.X - fx * size.Width), (int)Math.Round(screenAnchor.Y - fy * size.Height), size.Width, size.Height);
        Invalidate();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        if (keyData == (Keys.Control | Keys.C)) { ClipboardOutput.Copy(_image, ClipboardOutput.EncodePng(_image)); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _image.Dispose();
            ContextMenuStrip?.Dispose();
        }
        base.Dispose(disposing);
    }

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int msg, int wParam, int lParam);
}
