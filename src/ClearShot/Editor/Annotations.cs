using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ClearShot.Editor;

/// <summary>The drawing tools in the editor's side bar. None means dragging inside the area moves it.</summary>
internal enum Tool { None, Pen, Line, Arrow, Rectangle, Highlighter, Text, Step, Pixelate }

/// <summary>
/// Something drawn on a screenshot. Coordinates are in the pixels of the captured monitor, so drawings stay
/// on the picture when the area is moved or resized, and the saved PNG matches the screen exactly.
/// </summary>
internal abstract class Annotation
{
    public Color Color { get; set; }

    /// <summary>Line thickness, or text / circle size, in image pixels.</summary>
    public float Size { get; set; }

    /// <summary>Draws onto <paramref name="target"/>, which <paramref name="g"/> draws into (pixelate reads it back).</summary>
    public abstract void Draw(Graphics g, Bitmap target);

    /// <summary>The area this touches, for repainting only what changed.</summary>
    public abstract Rectangle Bounds { get; }

    /// <summary>True if a click at <paramref name="p"/> lands on this drawing (within <paramref name="tolerance"/> pixels).</summary>
    public abstract bool Hit(PointF p, float tolerance);

    public abstract void Offset(float dx, float dy);

    protected static PointF Shift(PointF p, float dx, float dy) => new(p.X + dx, p.Y + dy);

    protected static float SegmentDistance(PointF p, PointF a, PointF b)
    {
        float vx = b.X - a.X, vy = b.Y - a.Y, lengthSq = vx * vx + vy * vy;
        float t = lengthSq == 0 ? 0 : Math.Clamp(((p.X - a.X) * vx + (p.Y - a.Y) * vy) / lengthSq, 0, 1);
        float cx = a.X + t * vx - p.X, cy = a.Y + t * vy - p.Y;
        return MathF.Sqrt(cx * cx + cy * cy);
    }

    protected static Rectangle Grow(RectangleF r, float by) =>
        Rectangle.FromLTRB((int)Math.Floor(r.Left - by), (int)Math.Floor(r.Top - by), (int)Math.Ceiling(r.Right + by), (int)Math.Ceiling(r.Bottom + by));

    protected static RectangleF Span(IReadOnlyList<PointF> points)
    {
        float l = float.MaxValue, t = float.MaxValue, r = float.MinValue, b = float.MinValue;
        foreach (var p in points) { l = Math.Min(l, p.X); t = Math.Min(t, p.Y); r = Math.Max(r, p.X); b = Math.Max(b, p.Y); }
        return points.Count == 0 ? RectangleF.Empty : RectangleF.FromLTRB(l, t, r, b);
    }

    protected static RectangleF Normalise(PointF a, PointF b) =>
        RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
}

/// <summary>Freehand pen, or a see-through highlighter.</summary>
internal sealed class Stroke : Annotation
{
    public const int HighlighterAlpha = 110;
    public List<PointF> Points { get; } = [];
    public bool Highlighter { get; init; }

    public override void Draw(Graphics g, Bitmap target)
    {
        if (Points.Count == 0) return;
        var color = Highlighter ? Color.FromArgb(HighlighterAlpha, Color) : Color;
        float width = Highlighter ? Size * 4 : Size;
        using var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        if (Highlighter) { pen.StartCap = LineCap.Square; pen.EndCap = LineCap.Square; }
        if (Points.Count == 1)
        {
            using var dot = new SolidBrush(color);
            g.FillEllipse(dot, Points[0].X - width / 2, Points[0].Y - width / 2, width, width);
            return;
        }
        // One path, so a highlighter crossing itself doesn't get darker where it overlaps.
        using var path = new GraphicsPath();
        path.AddLines(Points.ToArray());
        g.DrawPath(pen, path);
    }

    public override Rectangle Bounds => Grow(Span(Points), (Highlighter ? Size * 4 : Size) / 2 + 2);

    public override bool Hit(PointF p, float tolerance)
    {
        float reach = (Highlighter ? Size * 4 : Size) / 2 + tolerance;
        if (Points.Count == 1) return SegmentDistance(p, Points[0], Points[0]) <= reach;
        for (int i = 1; i < Points.Count; i++)
            if (SegmentDistance(p, Points[i - 1], Points[i]) <= reach) return true;
        return false;
    }

    public override void Offset(float dx, float dy)
    {
        for (int i = 0; i < Points.Count; i++) Points[i] = Shift(Points[i], dx, dy);
    }
}

/// <summary>A straight line, optionally with an arrowhead at the end.</summary>
internal sealed class LineShape : Annotation
{
    public PointF Start { get; set; }
    public PointF End { get; set; }
    public bool Arrow { get; init; }

    public float HeadLength => Math.Max(12, Size * 5);

    public override void Draw(Graphics g, Bitmap target)
    {
        float dx = End.X - Start.X, dy = End.Y - Start.Y, length = MathF.Sqrt(dx * dx + dy * dy);
        using var pen = new Pen(Color, Size) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        if (!Arrow || length < 1)
        {
            g.DrawLine(pen, Start, End);
            return;
        }
        float head = Math.Min(HeadLength, length);
        float ux = dx / length, uy = dy / length;
        // The shaft stops inside the head so its round end doesn't poke out of the point.
        var shaftEnd = new PointF(End.X - ux * head * 0.8f, End.Y - uy * head * 0.8f);
        g.DrawLine(pen, Start, shaftEnd);
        float half = head * 0.5f;
        var baseCentre = new PointF(End.X - ux * head, End.Y - uy * head);
        PointF[] tip =
        [
            End,
            new(baseCentre.X - uy * half, baseCentre.Y + ux * half),
            new(baseCentre.X + uy * half, baseCentre.Y - ux * half),
        ];
        using var brush = new SolidBrush(Color);
        g.FillPolygon(brush, tip);
    }

    public override Rectangle Bounds => Grow(Normalise(Start, End), Size + (Arrow ? HeadLength : 0) + 2);

    public override bool Hit(PointF p, float tolerance) =>
        SegmentDistance(p, Start, End) <= Size / 2 + tolerance + (Arrow && SegmentDistance(p, End, End) <= HeadLength ? HeadLength / 2 : 0);

    public override void Offset(float dx, float dy) { Start = Shift(Start, dx, dy); End = Shift(End, dx, dy); }
}

internal sealed class RectangleShape : Annotation
{
    public PointF Start { get; set; }
    public PointF End { get; set; }

    public override void Draw(Graphics g, Bitmap target)
    {
        var r = Normalise(Start, End);
        using var pen = new Pen(Color, Size) { LineJoin = LineJoin.Miter };
        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
    }

    public override Rectangle Bounds => Grow(Normalise(Start, End), Size + 2);

    public override bool Hit(PointF p, float tolerance)
    {
        var r = Normalise(Start, End);
        PointF a = new(r.Left, r.Top), b = new(r.Right, r.Top), c = new(r.Right, r.Bottom), d = new(r.Left, r.Bottom);
        float reach = Size / 2 + tolerance;
        return SegmentDistance(p, a, b) <= reach || SegmentDistance(p, b, c) <= reach
            || SegmentDistance(p, c, d) <= reach || SegmentDistance(p, d, a) <= reach;
    }

    public override void Offset(float dx, float dy) { Start = Shift(Start, dx, dy); End = Shift(End, dx, dy); }
}

/// <summary>A numbered circle: 1, 2, 3... for step-by-step guides.</summary>
internal sealed class StepMarker : Annotation
{
    public PointF Centre { get; set; }
    public int Number { get; init; }

    public float Diameter => Math.Max(22, Size * 7);

    public override void Draw(Graphics g, Bitmap target)
    {
        float d = Diameter;
        var circle = new RectangleF(Centre.X - d / 2, Centre.Y - d / 2, d, d);
        using var fill = new SolidBrush(Color);
        g.FillEllipse(fill, circle);
        using var ring = new Pen(Color.White, Math.Max(1.5f, d / 14));
        g.DrawEllipse(ring, circle);
        using var font = new Font("Segoe UI", d * 0.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var ink = new SolidBrush(IsLight(Color) ? Color.Black : Color.White);
        using var centred = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(Number.ToString(), font, ink, circle, centred);
    }

    internal static bool IsLight(Color c) => c.R * 0.299 + c.G * 0.587 + c.B * 0.114 > 160;

    public override Rectangle Bounds => Grow(new RectangleF(Centre.X - Diameter / 2, Centre.Y - Diameter / 2, Diameter, Diameter), 3);

    public override bool Hit(PointF p, float tolerance) => SegmentDistance(p, Centre, Centre) <= Diameter / 2 + tolerance;

    public override void Offset(float dx, float dy) => Centre = Shift(Centre, dx, dy);
}

internal sealed class TextNote : Annotation
{
    public PointF Origin { get; set; }
    public string Text { get; set; } = "";
    public string FontName { get; set; } = "Segoe UI";
    public bool Bold { get; set; } = true;

    internal Font MakeFont()
    {
        try
        {
            using var family = new FontFamily(FontName);
            var style = Bold && family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold
                : family.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular
                : family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Italic;
            return new Font(family, Size, style, GraphicsUnit.Pixel);
        }
        catch (ArgumentException)
        {
            // The font was uninstalled or can't be used: fall back rather than fail.
            return new Font("Segoe UI", Size, Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        }
    }

    public override void Draw(Graphics g, Bitmap target)
    {
        if (Text.Length == 0) return;
        using var font = MakeFont();
        using var brush = new SolidBrush(Color);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.DrawString(Text, font, brush, Origin, StringFormat.GenericTypographic);
    }

    /// <summary>Where the text sits, and where the typing caret goes.</summary>
    public RectangleF Measure(out PointF caret)
    {
        using var font = MakeFont();
        using var scratch = new Bitmap(1, 1);
        using var g = Graphics.FromImage(scratch);
        var lines = Text.Split('\n');
        var size = g.MeasureString(Text.Length == 0 ? " " : Text, font, PointF.Empty, StringFormat.GenericTypographic);
        var last = g.MeasureString(lines[^1], font, PointF.Empty, StringFormat.GenericTypographic);
        float lineHeight = font.GetHeight(g);
        caret = new PointF(Origin.X + (lines[^1].Length == 0 ? 0 : last.Width), Origin.Y + lineHeight * (lines.Length - 1));
        return new RectangleF(Origin, new SizeF(Math.Max(size.Width, 1), Math.Max(size.Height, lineHeight)));
    }

    public override Rectangle Bounds => Grow(Measure(out _), Size * 0.5f + 4);

    public override bool Hit(PointF p, float tolerance)
    {
        var box = Measure(out _);
        box.Inflate(tolerance, tolerance);
        return box.Contains(p);
    }

    public override void Offset(float dx, float dy) => Origin = Shift(Origin, dx, dy);
}

/// <summary>Hides what's underneath (names, emails, wallet addresses) behind big solid blocks.</summary>
internal sealed class PixelateBox : Annotation
{
    public PointF Start { get; set; }
    public PointF End { get; set; }

    /// <summary>Blocks are large on purpose: small ones can leave text guessable.</summary>
    public int BlockSize { get; init; } = 14;

    public Rectangle Area => Rectangle.Round(Normalise(Start, End));

    public override void Draw(Graphics g, Bitmap target)
    {
        var area = Rectangle.Intersect(Area, new Rectangle(Point.Empty, target.Size));
        if (area.Width < 1 || area.Height < 1) return;
        g.Flush(FlushIntention.Sync);
        var data = target.LockBits(area, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                int block = Math.Max(2, BlockSize);
                for (int by = 0; by < area.Height; by += block)
                for (int bx = 0; bx < area.Width; bx += block)
                {
                    int bw = Math.Min(block, area.Width - bx), bh = Math.Min(block, area.Height - by);
                    long r = 0, gr = 0, b = 0;
                    for (int y = 0; y < bh; y++)
                    {
                        var row = (byte*)data.Scan0 + (by + y) * data.Stride + bx * 4;
                        for (int x = 0; x < bw; x++) { b += row[x * 4]; gr += row[x * 4 + 1]; r += row[x * 4 + 2]; }
                    }
                    int n = bw * bh;
                    byte ab = (byte)(b / n), ag = (byte)(gr / n), ar = (byte)(r / n);
                    for (int y = 0; y < bh; y++)
                    {
                        var row = (byte*)data.Scan0 + (by + y) * data.Stride + bx * 4;
                        for (int x = 0; x < bw; x++) { row[x * 4] = ab; row[x * 4 + 1] = ag; row[x * 4 + 2] = ar; row[x * 4 + 3] = 255; }
                    }
                }
            }
        }
        finally
        {
            target.UnlockBits(data);
        }
    }

    public override Rectangle Bounds => Grow(Normalise(Start, End), 2);

    public override bool Hit(PointF p, float tolerance)
    {
        var r = Normalise(Start, End);
        r.Inflate(tolerance, tolerance);
        return r.Contains(p);
    }

    public override void Offset(float dx, float dy) { Start = Shift(Start, dx, dy); End = Shift(End, dx, dy); }
}

/// <summary>
/// The picture being edited: the untouched monitor still, everything drawn on it, and a cached copy with the
/// finished drawings already baked in so the editor repaints quickly.
/// </summary>
internal sealed class EditDocument : IDisposable
{
    private readonly Bitmap _original;
    private readonly List<Annotation> _items = [];
    // Every change (draw, move, recolour, resize, delete) pushes how to take it back.
    private readonly Stack<Action> _undo = new();

    public EditDocument(Bitmap original)
    {
        _original = original;
        Baked = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);
        Rebake();
    }

    public Size Size => _original.Size;

    /// <summary>The still with every finished drawing on it.</summary>
    public Bitmap Baked { get; private set; }

    public IReadOnlyList<Annotation> Items => _items;

    public int NextStepNumber => _items.OfType<StepMarker>().Count() + 1;

    public void Add(Annotation item)
    {
        _items.Add(item);
        _undo.Push(() => _items.Remove(item));
        using var g = CreateGraphics(Baked);
        item.Draw(g, Baked);
    }

    /// <summary>The drawing on top at <paramref name="p"/>, if any.</summary>
    public Annotation? HitTest(PointF p, float tolerance)
    {
        for (int i = _items.Count - 1; i >= 0; i--)
            if (_items[i].Hit(p, tolerance)) return _items[i];
        return null;
    }

    /// <summary>Gives a finished drawing a new colour.</summary>
    public void Recolour(Annotation item, Color colour)
    {
        if (!_items.Contains(item) || item.Color.ToArgb() == colour.ToArgb()) return;
        var old = item.Color;
        item.Color = colour;
        _undo.Push(() => item.Color = old);
        Rebake();
    }

    /// <summary>Changes a finished drawing's thickness or text size.</summary>
    public void Resize(Annotation item, float size)
    {
        if (!_items.Contains(item) || item.Size == size) return;
        var old = item.Size;
        item.Size = size;
        _undo.Push(() => item.Size = old);
        Rebake();
    }

    public void Delete(Annotation item)
    {
        int index = _items.IndexOf(item);
        if (index < 0) return;
        _items.RemoveAt(index);
        _undo.Push(() => _items.Insert(Math.Min(index, _items.Count), item));
        Rebake();
    }

    /// <summary>Takes a drawing out while it's dragged. Returns its place in the stack, for <see cref="Drop"/>.</summary>
    public int Lift(Annotation item)
    {
        int index = _items.IndexOf(item);
        if (index >= 0) { _items.RemoveAt(index); Rebake(); }
        return index;
    }

    /// <summary>Puts a dragged drawing back in its old place; the move can be undone.</summary>
    public void Drop(Annotation item, int index, float movedX, float movedY)
    {
        _items.Insert(Math.Clamp(index, 0, _items.Count), item);
        if (movedX != 0 || movedY != 0) _undo.Push(() => item.Offset(-movedX, -movedY));
        Rebake();
    }

    /// <summary>Takes a drawing back out (to edit a piece of text again, for example).</summary>
    public void Remove(Annotation item)
    {
        if (_items.Remove(item)) Rebake();
    }

    /// <returns>The area to repaint, or empty if there was nothing to undo.</returns>
    public Rectangle Undo()
    {
        if (_undo.Count == 0) return Rectangle.Empty;
        _undo.Pop()();
        Rebake();
        return new Rectangle(Point.Empty, Size);
    }

    /// <summary>The final picture for <paramref name="area"/>, full resolution.</summary>
    public Bitmap Render(Rectangle area)
    {
        area.Intersect(new Rectangle(Point.Empty, Size));
        return Baked.Clone(area, PixelFormat.Format32bppArgb);
    }

    private void Rebake()
    {
        using var g = CreateGraphics(Baked);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImageUnscaled(_original, 0, 0);
        g.CompositingMode = CompositingMode.SourceOver;
        foreach (var item in _items) item.Draw(g, Baked);
    }

    internal static Graphics CreateGraphics(Bitmap target)
    {
        var g = Graphics.FromImage(target);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        return g;
    }

    public void Dispose() => Baked.Dispose();
}
