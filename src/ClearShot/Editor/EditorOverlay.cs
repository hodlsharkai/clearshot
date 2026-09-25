using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ClearShot.Editor;

internal enum EditAction { Cancel, Copy, Save, Done, Pin }

/// <param name="Area">The final area, in the pixels of the captured monitor.</param>
internal sealed record EditOutcome(EditAction Action, Rectangle Area);

/// <summary>
/// The "capture and edit" screen, Lightshot style. The picked area shows the still taken when the mouse was
/// released, with resize handles, a side bar of drawing tools and a bar of actions. The rest of the screen
/// stays live under a dim layer, so streams and videos keep playing.
/// </summary>
internal sealed class EditorOverlay : IDisposable
{
    internal static readonly (string Name, Color Color)[] Palette =
    [
        ("Red", Color.FromArgb(239, 68, 68)),
        ("Orange", Color.FromArgb(249, 115, 22)),
        ("Yellow", Color.FromArgb(250, 204, 21)),
        ("Green", Color.FromArgb(34, 197, 94)),
        ("Blue", Color.FromArgb(59, 130, 246)),
        ("Purple", Color.FromArgb(168, 85, 247)),
        ("White", Color.White),
        ("Black", Color.Black),
    ];

    private enum Grip { None, Move, N, S, E, W, NE, NW, SE, SW }

    private readonly EditDocument _doc;
    private readonly Rectangle _monitor;
    private readonly float _scale;
    private readonly int _pad, _handle, _gripReach;
    private readonly LiveRegionSelector.DimLayer _dim;
    private readonly Canvas _canvas;
    private readonly ToolBar _tools, _actions;
    private readonly TaskCompletionSource<EditOutcome> _result = new();

    private Rectangle _area;
    private Annotation? _drawing;
    private TextNote? _typing;
    private Grip _grip;
    private Point _gripStart;
    private Rectangle _areaAtGrip;
    private Point? _pointer;

    public Tool Tool { get; private set; } = Tool.None;
    public Color Colour { get; private set; } = Palette[0].Color;
    public float StrokeSize { get; private set; }
    public float TextSize { get; private set; }
    public string FontName { get; private set; } = "Segoe UI";
    public bool Bold { get; private set; } = true;

    /// <summary>Raised with true while a colour or font dialog is open (so Esc goes to the dialog), then false.</summary>
    internal event Action<bool>? DialogOpen;

    private enum TextDrag { None, Move, Resize }
    private TextDrag _textDrag;
    private PointF _textDragStart, _textOriginAtStart;
    private float _textSizeAtStart;
    private RectangleF _textBoxAtStart;
    private readonly HintPill _hint;

    /// <param name="monitorBounds">Where the captured monitor sits on the desktop.</param>
    /// <param name="area">The picked area, in the monitor's own pixels.</param>
    public EditorOverlay(EditDocument doc, Rectangle monitorBounds, Rectangle area)
    {
        _doc = doc;
        _monitor = monitorBounds;
        _scale = Dpi.ScaleFor(monitorBounds);
        _handle = Math.Max(6, (int)Math.Round(7 * _scale));
        _pad = _handle / 2 + 1;
        _gripReach = Math.Max(5, (int)Math.Round(6 * _scale));
        StrokeSize = Math.Max(2, (int)Math.Round(3 * _scale));
        TextSize = (int)Math.Round(22 * _scale);

        _dim = new LiveRegionSelector.DimLayer(monitorBounds) { Cursor = Cursors.Default };
        _dim.MouseDown += (_, e) => PointerDown(_dim.PointToScreen(e.Location), e.Button);
        _dim.MouseMove += (_, e) => PointerMove(_dim.PointToScreen(e.Location), _dim);
        _dim.MouseUp += (_, e) => PointerUp();

        _canvas = new Canvas(this);
        _hint = new HintPill(monitorBounds, _scale);

        ToolBar.Item ToolItem(Tool tool, string tip, Action<Graphics, Pen, Brush> icon) =>
            new(tip, icon, _ => PickTool(tool), () => Tool == tool);
        _tools = new ToolBar(vertical: true, _scale,
        [
            ToolItem(Tool.Pen, "Pen (P)", Icons.Pen),
            ToolItem(Tool.Line, "Line (L)", Icons.Line),
            ToolItem(Tool.Arrow, "Arrow (A)", Icons.Arrow),
            ToolItem(Tool.Rectangle, "Rectangle (R)", Icons.Rectangle),
            ToolItem(Tool.Highlighter, "Highlighter (H)", Icons.Highlighter),
            ToolItem(Tool.Text, "Text (T): click to type, drag the corner square to resize, drag the text to move it", Icons.Text),
            new("Font and bold", Icons.Font, ShowFonts),
            ToolItem(Tool.Step, "Numbered steps (N)", Icons.Step),
            ToolItem(Tool.Pixelate, "Pixelate: hide names, emails, addresses (B)", Icons.Pixelate),
            new("Colour", Icons.Colour(() => Colour), ShowColours, SeparatorBefore: true),
            new("Undo (Ctrl+Z)", Icons.Undo, _ => Undo()),
        ]);
        _actions = new ToolBar(vertical: false, _scale,
        [
            new("Pin to screen", Icons.Pin, _ => Finish(EditAction.Pin)),
            new("Copy (Ctrl+C)", Icons.Copy, _ => Finish(EditAction.Copy)),
            new("Save (Ctrl+S)", Icons.Save, _ => Finish(EditAction.Save)),
            new("Copy and save (Enter)", Icons.Done, _ => Finish(EditAction.Done), SeparatorBefore: true, Primary: true),
            new("Close (Esc)", Icons.Close, _ => Finish(EditAction.Cancel)),
        ]);

        SetArea(area);
    }

    public Rectangle Area => _area;

    /// <summary>Tests turn this off so running them never pulls focus away from what the user is doing.</summary>
    internal bool TakeFocus { get; init; } = true;

    /// <summary>Shows the editor and completes when an action is picked or it's closed.</summary>
    public Task<EditOutcome> RunAsync()
    {
        PlaceWindows();
        _dim.Show();
        _dim.Bounds = _monitor;
        _canvas.Show();
        _hint.Show();
        UpdateHint();
        _tools.Show();
        _actions.Show();
        if (TakeFocus)
        {
            _canvas.Activate();
            TakeForeground(_canvas.Handle);
        }
        return _result.Task;
    }

    // ---- Area, handles and window layout --------------------------------------------------------------

    private void SetArea(Rectangle area)
    {
        _area = area;
        if (_canvas.IsHandleCreated || _canvas.Visible) PlaceWindows();
    }

    private void PlaceWindows()
    {
        var onScreen = new Rectangle(_area.X + _monitor.X, _area.Y + _monitor.Y, _area.Width, _area.Height);
        _dim.CutHole(_area);
        _canvas.Place(onScreen, _pad, _handle);
        int gap = (int)Math.Round(6 * _scale);

        // Side bar: right of the area, lined up with its bottom (like Lightshot); left if there's no room; inside as a last resort.
        var t = _tools.Size;
        int tx = onScreen.Right + gap;
        if (tx + t.Width > _monitor.Right) tx = onScreen.Left - gap - t.Width;
        if (tx < _monitor.Left) tx = onScreen.Right - gap - t.Width;
        int ty = Math.Clamp(onScreen.Bottom - t.Height, _monitor.Top, Math.Max(_monitor.Top, _monitor.Bottom - t.Height));
        _tools.Location = new Point(tx, ty);

        // Action bar: under the area, lined up with its right edge; above if there's no room; inside as a last resort.
        var a = _actions.Size;
        int ay = onScreen.Bottom + gap;
        if (ay + a.Height > _monitor.Bottom) ay = onScreen.Top - gap - a.Height;
        if (ay < _monitor.Top) ay = onScreen.Bottom - gap - a.Height;
        int ax = Math.Clamp(onScreen.Right - a.Width, _monitor.Left, Math.Max(_monitor.Left, _monitor.Right - a.Width));
        // If the side bar went inside the area too, keep the two from overlapping.
        var toolsRect = new Rectangle(_tools.Location, t);
        if (toolsRect.IntersectsWith(new Rectangle(ax, ay, a.Width, a.Height))) ax = Math.Max(_monitor.Left, tx - gap - a.Width);
        _actions.Location = new Point(ax, ay);
    }

    private Grip HitTest(Point image)
    {
        int r = _gripReach;
        bool nearL = Math.Abs(image.X - _area.Left) <= r, nearR = Math.Abs(image.X - _area.Right) <= r;
        bool nearT = Math.Abs(image.Y - _area.Top) <= r, nearB = Math.Abs(image.Y - _area.Bottom) <= r;
        bool withinX = image.X >= _area.Left - r && image.X <= _area.Right + r;
        bool withinY = image.Y >= _area.Top - r && image.Y <= _area.Bottom + r;
        if (nearT && nearL) return Grip.NW;
        if (nearT && nearR) return Grip.NE;
        if (nearB && nearL) return Grip.SW;
        if (nearB && nearR) return Grip.SE;
        if (nearT && withinX) return Grip.N;
        if (nearB && withinX) return Grip.S;
        if (nearL && withinY) return Grip.W;
        if (nearR && withinY) return Grip.E;
        if (_area.Contains(image) && Tool == Tool.None) return Grip.Move;
        return Grip.None;
    }

    private static Cursor CursorFor(Grip grip) => grip switch
    {
        Grip.N or Grip.S => Cursors.SizeNS,
        Grip.E or Grip.W => Cursors.SizeWE,
        Grip.NW or Grip.SE => Cursors.SizeNWSE,
        Grip.NE or Grip.SW => Cursors.SizeNESW,
        Grip.Move => Cursors.SizeAll,
        _ => Cursors.Default,
    };

    private Rectangle Resized(Point image)
    {
        int dx = image.X - _gripStart.X, dy = image.Y - _gripStart.Y;
        var bounds = new Rectangle(Point.Empty, _doc.Size);
        var a = _areaAtGrip;
        if (_grip == Grip.Move)
        {
            int x = Math.Clamp(a.X + dx, 0, bounds.Width - a.Width), y = Math.Clamp(a.Y + dy, 0, bounds.Height - a.Height);
            return new Rectangle(x, y, a.Width, a.Height);
        }
        int l = a.Left, t = a.Top, rr = a.Right, b = a.Bottom;
        if (_grip is Grip.W or Grip.NW or Grip.SW) l += dx;
        if (_grip is Grip.E or Grip.NE or Grip.SE) rr += dx;
        if (_grip is Grip.N or Grip.NW or Grip.NE) t += dy;
        if (_grip is Grip.S or Grip.SW or Grip.SE) b += dy;
        var r = Rectangle.FromLTRB(Math.Min(l, rr), Math.Min(t, b), Math.Max(l, rr), Math.Max(t, b));
        r.Intersect(bounds);
        if (r.Width < 4) r.Width = 4;
        if (r.Height < 4) r.Height = 4;
        return r;
    }

    // ---- Mouse --------------------------------------------------------------------------------------

    private Point ToImage(Point screen) => new(screen.X - _monitor.X, screen.Y - _monitor.Y);

    internal void PointerDown(Point screen, MouseButtons button)
    {
        var p = ToImage(screen);
        if (button == MouseButtons.Right)
        {
            // Right-click: finish the text being typed, otherwise close, as in the picker.
            if (_typing is not null) CommitText();
            else Finish(EditAction.Cancel);
            return;
        }
        if (button != MouseButtons.Left) return;

        // The text being typed: drag its corner square to resize it, drag the text itself to move it.
        if (_typing is not null)
        {
            if (TextHandle(_typing).Contains(p)) { StartTextDrag(TextDrag.Resize, p); return; }
            if (TextBox(_typing).Contains(p)) { StartTextDrag(TextDrag.Move, p); return; }
            CommitText();
        }
        // With the text tool, clicking existing text opens it again.
        if (Tool == Tool.Text && _doc.Items.OfType<TextNote>().LastOrDefault(n => TextBox(n).Contains(p)) is { } existing)
        {
            _doc.Remove(existing);
            _typing = existing;
            UpdateHint();
            StartTextDrag(TextDrag.Move, p);
            InvalidateImage(existing.Bounds);
            return;
        }

        var grip = HitTest(p);
        if (grip != Grip.None)
        {
            _grip = grip;
            _gripStart = p;
            _areaAtGrip = _area;
            return;
        }
        if (!_area.Contains(p) || Tool == Tool.None) return;

        PointF at = p;
        switch (Tool)
        {
            case Tool.Pen:
            case Tool.Highlighter:
                var stroke = new Stroke { Color = Colour, Size = StrokeSize, Highlighter = Tool == Tool.Highlighter };
                stroke.Points.Add(at);
                _drawing = stroke;
                break;
            case Tool.Line:
            case Tool.Arrow:
                _drawing = new LineShape { Color = Colour, Size = StrokeSize, Start = at, End = at, Arrow = Tool == Tool.Arrow };
                break;
            case Tool.Rectangle:
                _drawing = new RectangleShape { Color = Colour, Size = StrokeSize, Start = at, End = at };
                break;
            case Tool.Pixelate:
                _drawing = new PixelateBox { Start = at, End = at, BlockSize = Math.Max(10, (int)Math.Round(14 * _scale)) };
                break;
            case Tool.Step:
                var step = new StepMarker { Color = Colour, Size = StrokeSize, Centre = at, Number = _doc.NextStepNumber };
                _doc.Add(step);
                InvalidateImage(step.Bounds);
                break;
            case Tool.Text:
                _typing = new TextNote { Color = Colour, Size = TextSize, Origin = at, FontName = FontName, Bold = Bold };
                UpdateHint();
                InvalidateImage(_typing.Bounds);
                break;
        }
    }

    internal void PointerMove(Point screen, Control source)
    {
        var p = ToImage(screen);
        if (_grip != Grip.None)
        {
            SetArea(Resized(p));
            return;
        }
        if (_textDrag != TextDrag.None && _typing is not null)
        {
            DragText(p);
            return;
        }

        var hover = HitTest(p);
        source.Cursor = _typing is not null && TextHandle(_typing).Contains(p) ? Cursors.SizeNWSE
            : _typing is not null && TextBox(_typing).Contains(p) ? Cursors.SizeAll
            : hover != Grip.None ? CursorFor(hover)
            : !_area.Contains(p) ? Cursors.Default
            : Tool == Tool.Text ? Cursors.IBeam
            : Tool == Tool.None ? Cursors.Default
            : Cursors.Cross;

        var oldRing = RingBounds();
        _pointer = _area.Contains(p) ? p : null;
        var dirty = Rectangle.Union(oldRing, RingBounds());

        if (_drawing is not null)
        {
            var before = _drawing.Bounds;
            PointF at = p;
            switch (_drawing)
            {
                case Stroke s: s.Points.Add(at); break;
                case LineShape l: l.End = at; break;
                case RectangleShape r: r.End = at; break;
                case PixelateBox x: x.End = at; break;
            }
            dirty = Rectangle.Union(dirty, Rectangle.Union(before, _drawing.Bounds));
        }
        if (!dirty.IsEmpty) InvalidateImage(dirty);
    }

    internal void PointerUp()
    {
        _textDrag = TextDrag.None;
        if (_grip != Grip.None)
        {
            _grip = Grip.None;
            return;
        }
        if (_drawing is null) return;
        var done = _drawing;
        _drawing = null;
        bool worthKeeping = done switch
        {
            LineShape l => Distance(l.Start, l.End) >= 2,
            RectangleShape r => Math.Abs(r.End.X - r.Start.X) >= 2 && Math.Abs(r.End.Y - r.Start.Y) >= 2,
            PixelateBox x => x.Area.Width >= 2 && x.Area.Height >= 2,
            _ => true,
        };
        if (worthKeeping) _doc.Add(done);
        InvalidateImage(done.Bounds);
    }

    internal void PointerLeft()
    {
        var ring = RingBounds();
        _pointer = null;
        if (!ring.IsEmpty) InvalidateImage(ring);
    }

    internal void Wheel(int delta)
    {
        int steps = Math.Sign(delta);
        if (Tool == Tool.Text || _typing is not null)
        {
            TextSize = Math.Clamp(TextSize * (steps > 0 ? 1.1f : 1 / 1.1f), MinTextSize, MaxTextSize);
            if (_typing is not null)
            {
                var before = _typing.Bounds;
                _typing.Size = TextSize;
                InvalidateImage(Rectangle.Union(before, _typing.Bounds));
            }
        }
        else
        {
            var before = RingBounds();
            StrokeSize = Math.Clamp(StrokeSize + steps, 1, 40);
            InvalidateImage(Rectangle.Union(before, RingBounds()));
        }
    }

    private const float MinTextSize = 6, MaxTextSize = 2000;

    /// <summary>The dotted box around text, in image pixels.</summary>
    private static RectangleF TextBox(TextNote note)
    {
        var box = note.Measure(out _);
        return new RectangleF(box.X - 3, box.Y - 2, box.Width + 6, box.Height + 4);
    }

    /// <summary>The square on the text box's bottom-right corner that resizes the text.</summary>
    private RectangleF TextHandle(TextNote note)
    {
        var box = TextBox(note);
        float reach = _handle / 2f + _gripReach / 2f;
        return new RectangleF(box.Right - reach, box.Bottom - reach, reach * 2, reach * 2);
    }

    private void StartTextDrag(TextDrag drag, Point p)
    {
        if (_typing is null) return;
        _textDrag = drag;
        _textDragStart = p;
        _textOriginAtStart = _typing.Origin;
        _textSizeAtStart = _typing.Size;
        _textBoxAtStart = TextBox(_typing);
    }

    private void DragText(Point p)
    {
        if (_typing is null) return;
        var before = _typing.Bounds;
        float dx = p.X - _textDragStart.X, dy = p.Y - _textDragStart.Y;
        if (_textDrag == TextDrag.Move)
        {
            _typing.Origin = new PointF(_textOriginAtStart.X + dx, _textOriginAtStart.Y + dy);
        }
        else
        {
            // Any size: the text grows or shrinks with the corner, keeping its top-left where it is.
            var box = _textBoxAtStart;
            float factor = Math.Max((box.Width + dx) / box.Width, (box.Height + dy) / box.Height);
            _typing.Size = Math.Clamp(_textSizeAtStart * Math.Max(0.02f, factor), MinTextSize, MaxTextSize);
            TextSize = _typing.Size;
        }
        InvalidateImage(Rectangle.Union(before, _typing.Bounds));
    }

    private static float Distance(PointF a, PointF b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>A ring under the mouse showing how thick the next line will be.</summary>
    private Rectangle RingBounds()
    {
        if (_pointer is not Point p || _drawing is not null) return Rectangle.Empty;
        if (Tool is not (Tool.Pen or Tool.Line or Tool.Arrow or Tool.Rectangle or Tool.Highlighter)) return Rectangle.Empty;
        int d = (int)Math.Ceiling(RingDiameter) + 6;
        return new Rectangle(p.X - d / 2, p.Y - d / 2, d, d);
    }

    private float RingDiameter => Math.Max(4, Tool == Tool.Highlighter ? StrokeSize * 4 : StrokeSize);

    // ---- Keyboard -----------------------------------------------------------------------------------

    /// <returns>True if the key was used.</returns>
    internal bool Key(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        bool ctrl = (keyData & Keys.Control) != 0;

        if (_typing is not null)
        {
            if (key == Keys.Escape) { CommitText(); return true; }
            if (ctrl && key == Keys.Z) { var gone = _typing.Bounds; _typing = null; UpdateHint(); InvalidateImage(gone); return true; }
            if (ctrl && key == Keys.V && Clipboard.ContainsText()) { TypeText(Clipboard.GetText()); return true; }
            if (key == Keys.Back)
            {
                if (_typing.Text.Length > 0) { var before = _typing.Bounds; _typing.Text = _typing.Text[..^1]; InvalidateImage(before); }
                return true;
            }
            if (key == Keys.Enter) { TypeText("\n"); return true; }
            return false; // typed characters arrive through TypeChar
        }

        if (key == Keys.Escape) { Finish(EditAction.Cancel); return true; }
        if (key == Keys.Enter) { Finish(EditAction.Done); return true; }
        if (ctrl && key == Keys.C) { Finish(EditAction.Copy); return true; }
        if (ctrl && key == Keys.S) { Finish(EditAction.Save); return true; }
        if (ctrl && key == Keys.Z) { Undo(); return true; }
        if (ctrl || (keyData & Keys.Alt) != 0) return false;
        Tool? picked = key switch
        {
            Keys.P => Tool.Pen,
            Keys.L => Tool.Line,
            Keys.A => Tool.Arrow,
            Keys.R => Tool.Rectangle,
            Keys.H => Tool.Highlighter,
            Keys.T => Tool.Text,
            Keys.N => Tool.Step,
            Keys.B => Tool.Pixelate,
            _ => null,
        };
        if (picked is Tool t) { PickTool(t); return true; }
        return false;
    }

    internal void TypeChar(char c)
    {
        if (_typing is null || char.IsControl(c)) return;
        TypeText(c.ToString());
    }

    private void TypeText(string text)
    {
        if (_typing is null) return;
        var before = _typing.Bounds;
        _typing.Text += text.Replace("\r\n", "\n").Replace('\r', '\n');
        InvalidateImage(Rectangle.Union(before, _typing.Bounds));
    }

    private void CommitText()
    {
        if (_typing is null) return;
        var note = _typing;
        _typing = null;
        _textDrag = TextDrag.None;
        UpdateHint();
        if (note.Text.Trim().Length > 0) _doc.Add(note);
        InvalidateImage(note.Bounds);
    }

    // ---- Tools and actions --------------------------------------------------------------------------

    internal void PickTool(Tool tool)
    {
        CommitText();
        Tool = Tool == tool ? Tool.None : tool;
        _tools.Invalidate();
        _canvas.Invalidate();
    }

    internal void Undo()
    {
        if (_typing is not null) { var gone = _typing.Bounds; _typing = null; UpdateHint(); InvalidateImage(gone); return; }
        var changed = _doc.Undo();
        if (!changed.IsEmpty) InvalidateImage(changed);
    }

    internal void SetColour(Color colour)
    {
        Colour = colour;
        if (_typing is not null) { _typing.Color = colour; InvalidateImage(_typing.Bounds); }
        _tools.Invalidate();
    }

    private void ShowColours(Rectangle button)
    {
        var menu = new ContextMenuStrip { ShowImageMargin = true };
        foreach (var (name, color) in Palette)
        {
            var swatch = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(swatch))
            {
                using var fill = new SolidBrush(color);
                g.FillRectangle(fill, 1, 1, 14, 14);
                g.DrawRectangle(Pens.Gray, 1, 1, 13, 13);
            }
            var item = new ToolStripMenuItem(name, swatch, (_, _) => SetColour(color)) { Checked = color.ToArgb() == Colour.ToArgb() };
            menu.Items.Add(item);
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("More colours…", null, (_, _) =>
        {
            using var dialog = new ColorDialog { Color = Colour, FullOpen = true, AnyColor = true };
            DialogOpen?.Invoke(true);
            try { if (dialog.ShowDialog(_canvas) == DialogResult.OK) SetColour(dialog.Color); }
            finally { DialogOpen?.Invoke(false); }
            _canvas.Activate();
        });
        menu.Closed += (_, _) =>
        {
            _canvas.BeginInvoke(() => { _canvas.Activate(); menu.Dispose(); });
        };
        menu.Show(new Point(button.Right, button.Top));
    }

    /// <summary>Opens the font list: each font shown in its own typeface, plus bold and every installed font.</summary>
    private void ShowFonts(Rectangle button)
    {
        var menu = new ContextMenuStrip();
        foreach (var name in QuickFonts.Where(IsInstalled))
        {
            Font? face = null;
            try { face = new Font(name, 11f); } catch (ArgumentException) { }
            var item = new ToolStripMenuItem(name, null, (_, _) => SetFont(name, Bold)) { Checked = name == FontName };
            if (face is not null) item.Font = face;
            menu.Items.Add(item);
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Bold", null, (_, _) => SetFont(FontName, !Bold)) { Checked = Bold });
        menu.Items.Add("More fonts…", null, (_, _) =>
        {
            using var dialog = new FontDialog { ShowEffects = false, ShowColor = false, FontMustExist = true };
            try { dialog.Font = new Font(FontName, 12f, Bold ? FontStyle.Bold : FontStyle.Regular); } catch (ArgumentException) { }
            DialogOpen?.Invoke(true);
            try { if (dialog.ShowDialog(_canvas) == DialogResult.OK) SetFont(dialog.Font.FontFamily.Name, dialog.Font.Bold); }
            finally { DialogOpen?.Invoke(false); }
            _canvas.Activate();
        });
        menu.Closed += (_, _) => _canvas.BeginInvoke(() => { _canvas.Activate(); menu.Dispose(); });
        menu.Show(new Point(button.Right, button.Top));
    }

    internal static readonly string[] QuickFonts =
    [
        "Segoe UI", "Arial", "Arial Black", "Impact", "Verdana", "Tahoma", "Trebuchet MS", "Georgia",
        "Times New Roman", "Courier New", "Consolas", "Comic Sans MS", "Segoe Print", "Segoe Script",
    ];

    private static bool IsInstalled(string name)
    {
        try { using var f = new FontFamily(name); return true; }
        catch (ArgumentException) { return false; }
    }

    internal void SetFont(string name, bool bold)
    {
        FontName = name;
        Bold = bold;
        if (_typing is not null)
        {
            var before = _typing.Bounds;
            _typing.FontName = name;
            _typing.Bold = bold;
            InvalidateImage(Rectangle.Union(before, _typing.Bounds));
        }
    }

    private void UpdateHint() => _hint.SetText(_typing is not null
        ? "Typing  ·  drag the corner square to resize  ·  Esc or click outside to finish"
        : "Enter: copy and save  ·  Esc or right-click: close  ·  Scroll: size");

    internal void Finish(EditAction action)
    {
        if (_result.Task.IsCompleted) return;
        CommitText();
        _drawing = null;
        _tools.Hide();
        _actions.Hide();
        _canvas.Hide();
        _dim.Hide();
        _hint.Hide();
        _result.TrySetResult(new EditOutcome(action, _area));
    }

    // ---- Painting -----------------------------------------------------------------------------------

    private void InvalidateImage(Rectangle image)
    {
        if (image.IsEmpty) return;
        image.Inflate(2, 2);
        _canvas.Invalidate(new Rectangle(image.X - _area.X + _pad, image.Y - _area.Y + _pad, image.Width, image.Height));
    }

    internal void Paint(Graphics g)
    {
        var dest = new Rectangle(_pad, _pad, _area.Width, _area.Height);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(_doc.Baked, dest, _area, GraphicsUnit.Pixel);
        g.CompositingMode = CompositingMode.SourceOver;

        var state = g.Save();
        g.SetClip(dest);
        g.TranslateTransform(_pad - _area.X, _pad - _area.Y);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        switch (_drawing)
        {
            case PixelateBox box:
                // The blocks are worked out on release; while dragging, show where they'll go.
                using (var black = new Pen(Color.Black, 1))
                using (var white = new Pen(Color.White, 1) { DashStyle = DashStyle.Dash })
                {
                    var r = box.Area;
                    g.DrawRectangle(black, r);
                    g.DrawRectangle(white, r);
                }
                break;
            case not null:
                _drawing.Draw(g, _doc.Baked);
                break;
        }
        if (_typing is not null)
        {
            _typing.Draw(g, _doc.Baked);
            var box = _typing.Measure(out var caret);
            using var dashed = new Pen(Color.FromArgb(180, 255, 255, 255), 1) { DashStyle = DashStyle.Dot };
            g.DrawRectangle(dashed, box.X - 3, box.Y - 2, box.Width + 6, box.Height + 4);
            using var caretPen = new Pen(_typing.Color, Math.Max(1.5f, _scale * 1.5f));
            using var font = _typing.MakeFont();
            g.DrawLine(caretPen, caret.X + 1, caret.Y + 1, caret.X + 1, caret.Y + font.GetHeight(g) - 1);
            // Corner square: drag it to make the text any size.
            var handle = TextHandle(_typing);
            float hs = _handle;
            var square = new RectangleF(handle.X + (handle.Width - hs) / 2, handle.Y + (handle.Height - hs) / 2, hs, hs);
            g.FillRectangle(Brushes.White, square);
            using var squareEdge = new Pen(Color.FromArgb(15, 23, 42));
            g.DrawRectangle(squareEdge, square.X, square.Y, square.Width, square.Height);
        }
        if (_pointer is Point p && !RingBounds().IsEmpty)
        {
            float d = RingDiameter;
            using var dark = new Pen(Color.FromArgb(160, 0, 0, 0), 3);
            using var light = new Pen(Color.White, 1);
            g.DrawEllipse(dark, p.X - d / 2, p.Y - d / 2, d, d);
            g.DrawEllipse(light, p.X - d / 2, p.Y - d / 2, d, d);
        }
        g.Restore(state);

        // Dashed outline and handles, as in the picker.
        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.None;
        using (var black = new Pen(Color.Black))
        using (var white = new Pen(Color.White) { DashStyle = DashStyle.Dash })
        {
            g.DrawRectangle(black, dest.X, dest.Y, dest.Width - 1, dest.Height - 1);
            g.DrawRectangle(white, dest.X, dest.Y, dest.Width - 1, dest.Height - 1);
        }
        foreach (var h in Canvas.Handles(dest, _handle))
        {
            g.FillRectangle(Brushes.White, h);
            using var edge = new Pen(Color.FromArgb(15, 23, 42));
            g.DrawRectangle(edge, h.X, h.Y, h.Width - 1, h.Height - 1);
        }
    }

    public void Dispose()
    {
        _tools.Dispose();
        _actions.Dispose();
        _canvas.Dispose();
        _dim.Dispose();
        _hint.Dispose();
    }

    // ---- Foreground ---------------------------------------------------------------------------------

    /// <summary>
    /// The editor needs the keyboard (typing text, Enter, Ctrl+Z). Windows only lets the app that owns the
    /// foreground hand it over, so borrow the foreground app's input queue for a moment.
    /// </summary>
    private static void TakeForeground(IntPtr window)
    {
        var current = GetForegroundWindow();
        if (current == window) return;
        uint theirs = GetWindowThreadProcessId(current, out _), ours = GetCurrentThreadId();
        bool attached = theirs != 0 && theirs != ours && AttachThreadInput(ours, theirs, true);
        try
        {
            SetForegroundWindow(window);
        }
        finally
        {
            if (attached) AttachThreadInput(ours, theirs, false);
        }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint to, bool on);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    /// <summary>A small note at the top of the screen saying how to finish or get out.</summary>
    private sealed class HintPill : Form
    {
        private const int WsExNoActivate = 0x08000000, WsExToolWindow = 0x80, WsExTopmost = 0x8, WsExTransparent = 0x20;
        private readonly Rectangle _monitor;
        private readonly float _scale;
        private string _text = "";

        public HintPill(Rectangle monitor, float scale)
        {
            _monitor = monitor;
            _scale = scale;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = ToolBar.Background;
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 13f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WsExNoActivate | WsExToolWindow | WsExTopmost | WsExTransparent;
                return cp;
            }
        }

        public void SetText(string text)
        {
            _text = text;
            var size = TextRenderer.MeasureText(text, Font);
            int w = size.Width + (int)(24 * _scale), h = size.Height + (int)(12 * _scale);
            Bounds = new Rectangle(_monitor.X + (_monitor.Width - w) / 2, _monitor.Y + (int)(24 * _scale), w, h);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e) =>
            TextRenderer.DrawText(e.Graphics, _text, Font, ClientRectangle, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    /// <summary>The window showing the still: shaped to the area plus its handles, so nothing else is covered.</summary>
    private sealed class Canvas : Form
    {
        private const int WsExToolWindow = 0x80, WsExTopmost = 0x8;
        private readonly EditorOverlay _owner;

        public Canvas(EditorOverlay owner)
        {
            _owner = owner;
            Text = "ClearShot editor";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            DoubleBuffered = true;
            KeyPreview = true;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WsExToolWindow | WsExTopmost;
                return cp;
            }
        }

        internal static IEnumerable<Rectangle> Handles(Rectangle r, int size)
        {
            int h = size / 2;
            int[] xs = [r.Left, r.Left + r.Width / 2, r.Right - 1], ys = [r.Top, r.Top + r.Height / 2, r.Bottom - 1];
            foreach (var y in ys)
            foreach (var x in xs)
                if (!(x == xs[1] && y == ys[1])) yield return new Rectangle(x - h, y - h, size, size);
        }

        public void Place(Rectangle areaOnScreen, int pad, int handle)
        {
            var bounds = Rectangle.Inflate(areaOnScreen, pad, pad);
            var inside = new Rectangle(pad, pad, areaOnScreen.Width, areaOnScreen.Height);
            var shape = new Region(inside);
            foreach (var h in Handles(inside, handle)) shape.Union(h);
            var old = Region;
            Bounds = bounds;
            Region = shape;
            old?.Dispose();
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e) => _owner.Paint(e.Graphics);

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            // Clicking the picture always gives the editor the keyboard back.
            if (_owner.TakeFocus && !Focused) { Activate(); TakeForeground(Handle); }
            _owner.PointerDown(PointToScreen(e.Location), e.Button);
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); _owner.PointerMove(PointToScreen(e.Location), this); }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _owner.PointerUp(); }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _owner.PointerLeft(); }

        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); _owner.Wheel(e.Delta); }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
            _owner.Key(keyData) || base.ProcessCmdKey(ref msg, keyData);

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            _owner.TypeChar(e.KeyChar);
            e.Handled = true;
        }
    }
}
