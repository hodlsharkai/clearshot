using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ClearShot;

/// <summary>
/// Covers one monitor with a frozen copy of what was on it, dimmed, and lets you drag out a box.
/// Drawn at 1:1 physical pixels so the selection is exact on 4K and scaled displays.
/// </summary>
internal sealed class RegionSelector : Form
{
    private static readonly Color Accent = Color.FromArgb(56, 189, 248);
    private readonly Bitmap _frozen;
    private readonly Bitmap _dimmed;
    private readonly Font _labelFont;
    private Point? _anchor;
    private Rectangle _selection;
    private Rectangle _lastPainted;
    private readonly Rectangle _monitorBounds;

    /// <summary>The chosen area, relative to the monitor, once the dialog returns OK.</summary>
    public Rectangle Selection { get; private set; }

    public RegionSelector(Bitmap frozen, Rectangle monitorBounds)
    {
        _frozen = frozen.Clone(new Rectangle(Point.Empty, frozen.Size), PixelFormat.Format32bppPArgb);
        _dimmed = new Bitmap(frozen.Width, frozen.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(_dimmed))
        {
            g.DrawImageUnscaled(_frozen, 0, 0);
            using var shade = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
            g.FillRectangle(shade, 0, 0, frozen.Width, frozen.Height);
        }

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = monitorBounds;
        TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None;
        _labelFont = new Font("Segoe UI", 13f * Dpi.ScaleFor(monitorBounds), FontStyle.Regular, GraphicsUnit.Pixel);
        _monitorBounds = monitorBounds;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // DPI handling can nudge a window while it is created on a scaled monitor; pin it back exactly.
        Bounds = _monitorBounds;
        Activate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Everything is painted in OnPaint.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        var clip = e.ClipRectangle;
        g.DrawImage(_dimmed, clip, clip, GraphicsUnit.Pixel);

        if (_selection.Width > 0 && _selection.Height > 0)
        {
            var bright = Rectangle.Intersect(_selection, clip);
            if (!bright.IsEmpty) g.DrawImage(_frozen, bright, bright, GraphicsUnit.Pixel);

            g.CompositingMode = CompositingMode.SourceOver;
            using var pen = new Pen(Accent, 1);
            g.DrawRectangle(pen, _selection.X, _selection.Y, _selection.Width - 1, _selection.Height - 1);
            DrawLabel(g, $"{_selection.Width} × {_selection.Height}", LabelRect());
        }
        else
        {
            g.CompositingMode = CompositingMode.SourceOver;
            var hint = "Drag to capture  ·  Esc to cancel";
            var size = g.MeasureString(hint, _labelFont).ToSize();
            DrawLabel(g, hint, new Rectangle((Width - size.Width) / 2 - 12, 24, size.Width + 24, size.Height + 12));
        }
    }

    private void DrawLabel(Graphics g, string text, Rectangle box)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var bg = new SolidBrush(Color.FromArgb(220, 15, 23, 42));
        g.FillRectangle(bg, box);
        TextRenderer.DrawText(g, text, _labelFont, box, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private Rectangle LabelRect()
    {
        var size = TextRenderer.MeasureText("0000 × 0000", _labelFont);
        var w = size.Width + 16;
        var h = size.Height + 8;
        int x = Math.Clamp(_selection.Right - w, 0, Math.Max(0, Width - w));
        int y = _selection.Bottom + 6;
        if (y + h > Height) y = _selection.Bottom - h - 6;
        return new Rectangle(x, Math.Max(0, y), w, h);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) { Cancel(); return; }
        if (e.Button != MouseButtons.Left) return;
        _anchor = e.Location;
        UpdateSelection(new Rectangle(e.Location, Size.Empty));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_anchor is not Point a) return;
        var p = new Point(Math.Clamp(e.X, 0, Width), Math.Clamp(e.Y, 0, Height));
        UpdateSelection(Rectangle.FromLTRB(Math.Min(a.X, p.X), Math.Min(a.Y, p.Y), Math.Max(a.X, p.X), Math.Max(a.Y, p.Y)));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _anchor is null) return;
        _anchor = null;
        if (_selection.Width >= 2 && _selection.Height >= 2)
        {
            Selection = _selection;
            DialogResult = DialogResult.OK;
            Close();
        }
        else
        {
            UpdateSelection(Rectangle.Empty);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Cancel();
        base.OnKeyDown(e);
    }

    private void Cancel()
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void UpdateSelection(Rectangle next)
    {
        _selection = next;
        // Repaint only what changed: the old box, the new box and their size labels (plus the hint area).
        var dirty = Rectangle.Union(_lastPainted, Rectangle.Union(next, LabelRect()));
        dirty = Rectangle.Union(dirty, new Rectangle(0, 0, Width, 80));
        dirty.Inflate(4, 4);
        _lastPainted = Rectangle.Union(next, LabelRect());
        Invalidate(dirty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _frozen.Dispose();
            _dimmed.Dispose();
            _labelFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
