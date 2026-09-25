using System.Drawing.Drawing2D;

namespace ClearShot.Editor;

/// <summary>
/// A strip of icon buttons beside the area being edited. It never takes focus, so the keyboard stays with the
/// editor (typing text, Ctrl+Z, Enter) while buttons are clicked.
/// </summary>
internal sealed class ToolBar : Form
{
    /// <param name="Paint">Draws the icon inside a 16 × 16 design box.</param>
    /// <param name="Selected">True when this button should show as picked (the current tool).</param>
    /// <param name="Primary">Filled with the accent colour: the one main action.</param>
    internal sealed record Item(string Tip, Action<Graphics, Pen, Brush> Paint, Action<Rectangle> Click,
        Func<bool>? Selected = null, bool SeparatorBefore = false, bool Primary = false);

    internal static readonly Color Background = Color.FromArgb(15, 23, 42);
    internal static readonly Color Border = Color.FromArgb(51, 65, 85);
    internal static readonly Color Hover = Color.FromArgb(30, 41, 59);
    internal static readonly Color Ink = Color.FromArgb(226, 232, 240);
    internal static readonly Color Accent = Color.FromArgb(56, 189, 248);

    private const int WsExNoActivate = 0x08000000, WsExToolWindow = 0x80, WsExTopmost = 0x8;
    private readonly List<Item> _items;
    private readonly List<Rectangle> _slots = [];
    private readonly bool _vertical;
    private readonly int _button, _gap, _pad;
    private readonly ToolTip _tips = new() { ShowAlways = true, InitialDelay = 350 };
    private int _hover = -1;

    public ToolBar(bool vertical, float scale, IEnumerable<Item> items)
    {
        _vertical = vertical;
        _items = items.ToList();
        _button = (int)Math.Round(30 * scale);
        _gap = (int)Math.Round(7 * scale);
        _pad = Math.Max(2, (int)Math.Round(3 * scale));
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Background;
        DoubleBuffered = true;
        Cursor = Cursors.Hand;

        int along = _pad;
        foreach (var item in _items)
        {
            if (item.SeparatorBefore && along > _pad) along += _gap;
            _slots.Add(vertical ? new Rectangle(_pad, along, _button, _button) : new Rectangle(along, _pad, _button, _button));
            along += _button;
        }
        along += _pad;
        int across = _button + _pad * 2;
        Size = vertical ? new Size(across, along) : new Size(along, across);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExNoActivate | WsExToolWindow | WsExTopmost;
            return cp;
        }
    }

    /// <summary>Where a button sits on screen (to open a menu under it, for example).</summary>
    public Rectangle ScreenRectOf(int index) => RectangleToScreen(_slots[index]);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Background);
        using (var border = new Pen(Border)) g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var slot = _slots[i];
            if (item.SeparatorBefore && i > 0)
            {
                using var line = new Pen(Border);
                if (_vertical) g.DrawLine(line, slot.X + _pad, slot.Y - _gap / 2 - 1, slot.Right - _pad, slot.Y - _gap / 2 - 1);
                else g.DrawLine(line, slot.X - _gap / 2 - 1, slot.Y + _pad, slot.X - _gap / 2 - 1, slot.Bottom - _pad);
            }
            bool selected = item.Selected?.Invoke() == true;
            var ink = Ink;
            if (item.Primary)
            {
                using var fill = new SolidBrush(i == _hover ? Color.FromArgb(125, 211, 252) : Accent);
                g.FillRectangle(fill, slot);
                ink = Background;
            }
            else if (selected)
            {
                using var fill = new SolidBrush(Color.FromArgb(28, 74, 96));
                g.FillRectangle(fill, slot);
                using var edge = new Pen(Accent);
                g.DrawRectangle(edge, slot.X, slot.Y, slot.Width - 1, slot.Height - 1);
                ink = Accent;
            }
            else if (i == _hover)
            {
                using var fill = new SolidBrush(Hover);
                g.FillRectangle(fill, slot);
            }
            DrawIcon(g, item, slot, ink);
        }
    }

    private void DrawIcon(Graphics g, Item item, Rectangle slot, Color ink)
    {
        var state = g.Save();
        float s = slot.Width * 0.56f / 16f;
        g.TranslateTransform(slot.X + (slot.Width - 16 * s) / 2, slot.Y + (slot.Height - 16 * s) / 2);
        g.ScaleTransform(s, s);
        using var pen = new Pen(ink, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(ink);
        item.Paint(g, pen, brush);
        g.Restore(state);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int over = _slots.FindIndex(r => r.Contains(e.Location));
        if (over == _hover) return;
        _hover = over;
        _tips.SetToolTip(this, over >= 0 ? _items[over].Tip : null);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        int hit = _slots.FindIndex(r => r.Contains(e.Location));
        if (hit >= 0) _items[hit].Click(ScreenRectOf(hit));
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tips.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Simple line icons, drawn in a 16 × 16 box. No emoji or icon fonts, so they look the same everywhere.</summary>
internal static class Icons
{
    public static void Select(Graphics g, Pen p, Brush b) =>
        g.DrawPolygon(p, [new PointF(4, 2), new PointF(12.5f, 10), new PointF(8.5f, 10.5f), new PointF(10.5f, 14.5f), new PointF(8.5f, 15), new PointF(6.5f, 11.5f), new PointF(4, 14)]);

    public static void Pen(Graphics g, Pen p, Brush b)
    {
        g.DrawPolygon(p, [new PointF(11, 2.5f), new PointF(13.5f, 5), new PointF(5, 13.5f), new PointF(2.5f, 13.5f), new PointF(2.5f, 11)]);
        g.DrawLine(p, 9.5f, 4, 12, 6.5f);
    }

    public static void Line(Graphics g, Pen p, Brush b) => g.DrawLine(p, 3, 13, 13, 3);

    public static void Arrow(Graphics g, Pen p, Brush b)
    {
        g.DrawLine(p, 3, 13, 13, 3);
        g.DrawLines(p, [new PointF(7.5f, 3), new PointF(13, 3), new PointF(13, 8.5f)]);
    }

    public static void Rectangle(Graphics g, Pen p, Brush b) => g.DrawRectangle(p, 2.5f, 4, 11, 8);

    public static void Highlighter(Graphics g, Pen p, Brush b)
    {
        g.DrawPolygon(p, [new PointF(5, 9.5f), new PointF(10.5f, 3), new PointF(13, 5.5f), new PointF(7, 11.5f)]);
        using var band = new Pen(Color.FromArgb(150, p.Color), 2.2f);
        g.DrawLine(band, 2, 14, 14, 14);
    }

    public static void Text(Graphics g, Pen p, Brush b)
    {
        g.DrawLine(p, 3, 3, 13, 3);
        g.DrawLine(p, 8, 3, 8, 13.5f);
    }

    public static void Font(Graphics g, Pen p, Brush b)
    {
        using var serif = new System.Drawing.Font("Georgia", 11f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var sans = new System.Drawing.Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Pixel);
        g.DrawString("A", serif, b, -0.5f, 1.5f);
        g.DrawString("a", sans, b, 8f, 4.5f);
    }

    public static void Eraser(Graphics g, Pen p, Brush b)
    {
        // A tilted eraser block with its rubbing end shaded, over a line.
        g.DrawPolygon(p, [new PointF(6, 13), new PointF(2.5f, 9.5f), new PointF(9.5f, 2.5f), new PointF(13.5f, 6.5f), new PointF(7, 13)]);
        g.FillPolygon(b, [new PointF(6, 13), new PointF(2.5f, 9.5f), new PointF(6, 6), new PointF(9.8f, 9.8f), new PointF(7, 13)]);
        g.DrawLine(p, 8, 13.5f, 14, 13.5f);
    }

    public static void Step(Graphics g, Pen p, Brush b)
    {
        g.DrawEllipse(p, 2, 2, 12, 12);
        using var font = new Font("Segoe UI", 8.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var centred = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("1", font, b, new RectangleF(2, 2.5f, 12, 12), centred);
    }

    public static void Pixelate(Graphics g, Pen p, Brush b)
    {
        g.DrawRectangle(p, 2, 2, 12, 12);
        foreach (var (x, y) in new[] { (2, 2), (10, 2), (6, 6), (2, 10), (10, 10) })
            g.FillRectangle(b, x, y, 4, 4);
    }

    public static Action<Graphics, Pen, Brush> Colour(Func<Color> current) => (g, p, b) =>
    {
        using var fill = new SolidBrush(current());
        g.FillRectangle(fill, 2.5f, 2.5f, 11, 11);
        using var edge = new Pen(Color.FromArgb(200, 255, 255, 255), 1f);
        g.DrawRectangle(edge, 2.5f, 2.5f, 11, 11);
    };

    public static void Undo(Graphics g, Pen p, Brush b)
    {
        g.DrawLines(p, [new PointF(5.5f, 2.5f), new PointF(2.5f, 5.5f), new PointF(5.5f, 8.5f)]);
        g.DrawBezier(p, new PointF(2.5f, 5.5f), new PointF(12, 5.5f), new PointF(15, 13), new PointF(7, 13.5f));
    }

    public static void Copy(Graphics g, Pen p, Brush b)
    {
        // Only the part of the back sheet that shows behind the front one.
        g.DrawLines(p, [new PointF(5.5f, 5), new PointF(5.5f, 2.5f), new PointF(13.5f, 2.5f), new PointF(13.5f, 11.5f), new PointF(10.5f, 11.5f)]);
        g.DrawRectangle(p, 2.5f, 5, 8, 9);
    }

    public static void Save(Graphics g, Pen p, Brush b)
    {
        g.DrawLine(p, 8, 2, 8, 10);
        g.DrawLines(p, [new PointF(4.5f, 6.5f), new PointF(8, 10), new PointF(11.5f, 6.5f)]);
        g.DrawLines(p, [new PointF(2.5f, 11), new PointF(2.5f, 13.5f), new PointF(13.5f, 13.5f), new PointF(13.5f, 11)]);
    }

    public static void Pin(Graphics g, Pen p, Brush b)
    {
        g.DrawPolygon(p, [new PointF(5.5f, 2), new PointF(10.5f, 2), new PointF(10, 6.5f), new PointF(12.5f, 9.5f), new PointF(3.5f, 9.5f), new PointF(6, 6.5f)]);
        g.DrawLine(p, 8, 9.5f, 8, 14.5f);
    }

    public static void Done(Graphics g, Pen p, Brush b)
    {
        using var thick = new Pen(p.Color, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(thick, [new PointF(3, 8.5f), new PointF(6.5f, 12), new PointF(13, 4)]);
    }

    public static void Close(Graphics g, Pen p, Brush b)
    {
        g.DrawLine(p, 4, 4, 12, 12);
        g.DrawLine(p, 12, 4, 4, 12);
    }
}
