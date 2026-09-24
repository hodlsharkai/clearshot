using System.Drawing.Drawing2D;

namespace ClearShot;

/// <summary>
/// Picks a region on the live screen: nothing freezes, videos and animated wallpapers keep moving.
/// Two windows cover the monitor, neither takes focus (so games and players keep running):
/// a dimming layer with a hole cut where the box is, and a click-through layer that draws the box
/// outline and its size. The screenshot itself is taken after the windows have gone.
/// </summary>
internal sealed class LiveRegionSelector : IDisposable
{
    private readonly DimLayer _dim;
    private readonly FrameLayer _frame;
    private readonly Rectangle _monitorBounds;
    private readonly TaskCompletionSource<Rectangle?> _result = new();
    private Point? _anchor;

    public LiveRegionSelector(Rectangle monitorBounds)
    {
        _monitorBounds = monitorBounds;
        _dim = new DimLayer(monitorBounds);
        _frame = new FrameLayer(monitorBounds);
        _dim.MouseDown += OnMouseDown;
        _dim.MouseMove += OnMouseMove;
        _dim.MouseUp += OnMouseUp;
    }

    /// <summary>Completes with the chosen area (relative to the monitor), or null if cancelled.</summary>
    public Task<Rectangle?> SelectAsync()
    {
        _dim.Show();
        _frame.Show();
        _dim.Bounds = _monitorBounds;
        _frame.Bounds = _monitorBounds;
        return _result.Task;
    }

    public void Cancel() => Finish(null);

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) { Cancel(); return; }
        if (e.Button != MouseButtons.Left) return;
        _anchor = e.Location;
        Update(new Rectangle(e.Location, Size.Empty));
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (_anchor is not Point a) return;
        var p = new Point(Math.Clamp(e.X, 0, _monitorBounds.Width), Math.Clamp(e.Y, 0, _monitorBounds.Height));
        Update(Rectangle.FromLTRB(Math.Min(a.X, p.X), Math.Min(a.Y, p.Y), Math.Max(a.X, p.X), Math.Max(a.Y, p.Y)));
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _anchor is null) return;
        _anchor = null;
        var chosen = _frame.Selection;
        if (chosen.Width >= 2 && chosen.Height >= 2) Finish(chosen);
        else Update(Rectangle.Empty);
    }

    private void Update(Rectangle selection)
    {
        _dim.CutHole(selection);
        _frame.SetSelection(selection);
    }

    private void Finish(Rectangle? chosen)
    {
        if (_result.Task.IsCompleted) return;
        _dim.Hide();
        _frame.Hide();
        _result.TrySetResult(chosen);
    }

    public void Dispose()
    {
        _dim.Dispose();
        _frame.Dispose();
    }

    private const int WsExNoActivate = 0x08000000, WsExToolWindow = 0x80, WsExTopmost = 0x8, WsExTransparent = 0x20, WsExLayered = 0x80000;

    /// <summary>Dark, see-through layer that catches the mouse. The box is a hole in it, so the inside is undimmed.</summary>
    private sealed class DimLayer : Form
    {
        private readonly Rectangle _full;

        public DimLayer(Rectangle bounds)
        {
            _full = new Rectangle(Point.Empty, bounds.Size);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            Opacity = 0.35;
            Cursor = Cursors.Cross;
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

        public void CutHole(Rectangle hole)
        {
            var old = Region;
            var region = new Region(_full);
            if (hole.Width > 0 && hole.Height > 0) region.Exclude(hole);
            Region = region;
            old?.Dispose();
        }
    }

    /// <summary>Click-through layer that draws the box outline and its size; everything else is see-through.</summary>
    private sealed class FrameLayer : Form
    {
        private static readonly Color Key = Color.FromArgb(255, 0, 255);
        private static readonly Color Accent = Color.FromArgb(56, 189, 248);
        private readonly Font _font;
        private Rectangle _lastDirty;

        public Rectangle Selection { get; private set; }

        public FrameLayer(Rectangle bounds)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Key;
            TransparencyKey = Key;
            DoubleBuffered = true;
            _font = new Font("Segoe UI", 13f * Dpi.ScaleFor(bounds), FontStyle.Regular, GraphicsUnit.Pixel);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WsExNoActivate | WsExToolWindow | WsExTopmost | WsExTransparent | WsExLayered;
                return cp;
            }
        }

        public void SetSelection(Rectangle selection)
        {
            Selection = selection;
            var dirty = Rectangle.Union(_lastDirty, Rectangle.Union(Inflate(selection), LabelRect()));
            dirty = Rectangle.Union(dirty, HintRect());
            _lastDirty = Rectangle.Union(Inflate(selection), LabelRect());
            Invalidate(dirty);
        }

        private static Rectangle Inflate(Rectangle r) { r.Inflate(4, 4); return r; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Key);
            if (Selection.Width > 0 && Selection.Height > 0)
            {
                using var pen = new Pen(Accent, 2) { Alignment = PenAlignment.Inset };
                g.DrawRectangle(pen, Selection);
                DrawPill(g, $"{Selection.Width} × {Selection.Height}", LabelRect());
            }
            else
            {
                DrawPill(g, "Drag to capture  ·  Esc to cancel", HintRect());
            }
        }

        private void DrawPill(Graphics g, string text, Rectangle box)
        {
            using var bg = new SolidBrush(Color.FromArgb(15, 23, 42));
            g.FillRectangle(bg, box);
            TextRenderer.DrawText(g, text, _font, box, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private Rectangle LabelRect()
        {
            var size = TextRenderer.MeasureText("0000 × 0000", _font);
            int w = size.Width + 16, h = size.Height + 8;
            int x = Math.Clamp(Selection.Right - w, 0, Math.Max(0, Width - w));
            int y = Selection.Bottom + 6;
            if (y + h > Height) y = Selection.Bottom - h - 6;
            return new Rectangle(x, Math.Max(0, y), w, h);
        }

        private Rectangle HintRect()
        {
            var size = TextRenderer.MeasureText("Drag to capture  ·  Esc to cancel", _font);
            return new Rectangle((Width - size.Width) / 2 - 12, 24, size.Width + 24, size.Height + 12);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _font.Dispose();
            base.Dispose(disposing);
        }
    }
}
