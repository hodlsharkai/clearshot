using System.Collections.Concurrent;
using System.Drawing.Drawing2D;

namespace ClearShot.Editor;

/// <summary>Every standard emoji ClearShot offers, from Unicode's own list (assets/emoji.txt), grouped as phones group them.</summary>
internal static class EmojiCatalog
{
    internal sealed record Entry(string Group, string Emoji, string Name);

    private static readonly Lazy<IReadOnlyList<Entry>> _all = new(() => Load().Where(e => EmojiRenderer.DrawsAsOne(e.Emoji)).ToList());

    /// <summary>Every emoji this PC can draw as one picture.</summary>
    public static IReadOnlyList<Entry> All => _all.Value;

    public static IReadOnlyList<Entry> Parse(string text, bool windowsCanDraw = true)
    {
        var list = new List<Entry>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#') continue;
            var parts = line.Split('\t');
            if (parts.Length < 3) continue;
            if (windowsCanDraw && !WindowsCanDraw(parts[1])) continue;
            list.Add(new Entry(parts[0], parts[1], parts[2]));
        }
        return list;
    }

    /// <summary>
    /// Windows' emoji font has no country or region flags (they show as two letters, "GB"), so those are left out.
    /// Other flags (🏁 🚩 🏳️‍🌈) are fine.
    /// </summary>
    internal static bool WindowsCanDraw(string emoji)
    {
        for (int i = 0; i < emoji.Length; i += char.IsSurrogatePair(emoji, i) ? 2 : 1)
        {
            int cp = char.ConvertToUtf32(emoji, i);
            if (cp is >= 0x1F1E6 and <= 0x1F1FF) return false; // regional indicator letters
            if (cp is >= 0xE0020 and <= 0xE007F) return false; // tag characters (England, Scotland, Wales)
        }
        return true;
    }

    private static IReadOnlyList<Entry> Load()
    {
        using var stream = typeof(EmojiCatalog).Assembly.GetManifestResourceStream("emoji.txt");
        if (stream is null) return [];
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Emoji whose name contains every word typed (so "red heart" and "heart red" both find ❤️).</summary>
    public static IReadOnlyList<Entry> Search(string query)
    {
        var words = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return All;
        return All.Where(e => words.All(w => e.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || e.Group.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
    }
}

/// <summary>
/// The emoji picker: a search box, a button per category, and a scrolling grid of every emoji, each drawn in colour.
/// "Frequently used" comes first. Click one to use it; Esc or clicking away closes it.
/// </summary>
internal sealed class EmojiPicker : Form
{
    private const int Columns = 10;
    private readonly TextBox _search;
    private readonly Panel _scroller;
    private readonly Grid _grid;
    private readonly FlowLayoutPanel _categories;
    private readonly float _scale;
    private bool _picked;

    public event Action<string>? Picked;

    // Every emoji picture the picker shows, drawn once. Filled in the background when the editor opens, so
    // scrolling through the list never stops to draw.
    internal static readonly ConcurrentDictionary<(string, int), Bitmap> Pictures = new();
    private static readonly ConcurrentDictionary<int, bool> Warmed = new();

    private static int CellFor(float scale) => (int)Math.Round(38 * scale);

    /// <summary>Starts drawing all the picker's emoji in the background (once per display scaling).</summary>
    public static void WarmUp(float scale)
    {
        int cell = CellFor(scale);
        if (!Warmed.TryAdd(cell, true)) return;
        var thread = new Thread(() =>
        {
            try
            {
                int gridPx = (int)Math.Round(cell * 0.62f), buttonPx = (int)Math.Round(cell * 0.55f);
                foreach (var e in EmojiRenderer.Quick) Pictures.GetOrAdd((e, gridPx), k => EmojiRenderer.RenderUncached(k.Item1, k.Item2));
                foreach (var group in EmojiCatalog.All.GroupBy(e => e.Group))
                    Pictures.GetOrAdd((group.First().Emoji, buttonPx), k => EmojiRenderer.RenderUncached(k.Item1, k.Item2));
                foreach (var entry in EmojiCatalog.All)
                    Pictures.GetOrAdd((entry.Emoji, gridPx), k => EmojiRenderer.RenderUncached(k.Item1, k.Item2));
            }
            catch (Exception ex)
            {
                Log.Write($"Emoji warm-up stopped: {ex.Message}");
            }
        }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Emoji warm-up" };
        thread.Start();
    }

    public EmojiPicker(float scale)
    {
        _scale = scale;
        int cell = CellFor(scale), pad = (int)Math.Round(8 * scale);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = ToolBar.Background;
        Padding = new Padding(1);

        _search = new TextBox
        {
            Dock = DockStyle.Top,
            PlaceholderText = "Search emoji",
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ToolBar.Hover,
            ForeColor = ToolBar.Ink,
            Font = new Font("Segoe UI", 11f * scale, FontStyle.Regular, GraphicsUnit.Pixel),
            Margin = Padding.Empty,
        };
        _categories = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, BackColor = ToolBar.Background, Padding = new Padding(pad / 2, pad / 2, 0, 0) };
        _grid = new Grid(cell, pad, scale) { Location = Point.Empty };
        _grid.Picked += e =>
        {
            _picked = true;
            Picked?.Invoke(e);
            Close();
        };
        _scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = ToolBar.Background };
        _scroller.Controls.Add(_grid);

        var sections = Sections("");
        foreach (var (name, items) in sections)
        {
            var button = new CategoryButton(items[0], name, cell);
            button.Click += (_, _) => ScrollTo(name);
            _categories.Controls.Add(button);
        }

        Controls.Add(_scroller);
        Controls.Add(_categories);
        Controls.Add(_search);
        int width = Columns * cell + pad * 2 + SystemInformation.VerticalScrollBarWidth + 2;
        Size = new Size(Math.Max(width, _categories.Controls.Count * (cell + 2) + pad), (int)Math.Round(420 * scale));
        _grid.Show(sections, width - SystemInformation.VerticalScrollBarWidth - 2);

        _search.TextChanged += (_, _) =>
        {
            _grid.Show(Sections(_search.Text), _scroller.ClientSize.Width);
            _scroller.AutoScrollPosition = Point.Empty;
        };
        Deactivate += (_, _) => { if (!_picked) BeginInvoke(Close); };
    }

    private static List<(string Name, List<string> Items)> Sections(string query)
    {
        if (!string.IsNullOrWhiteSpace(query))
            return [("Results", EmojiCatalog.Search(query).Select(e => e.Emoji).ToList())];
        var sections = new List<(string, List<string>)> { ("Frequently used", EmojiRenderer.Quick.ToList()) };
        foreach (var group in EmojiCatalog.All.GroupBy(e => e.Group))
            sections.Add((group.Key, group.Select(e => e.Emoji).ToList()));
        return sections;
    }

    private void ScrollTo(string section)
    {
        if (_search.Text.Length > 0) _search.Text = "";
        _scroller.AutoScrollPosition = new Point(0, _grid.SectionTop(section));
        _search.Focus();
    }

    /// <summary>Opens beside <paramref name="button"/> (screen coordinates), kept on the monitor.</summary>
    public void ShowNear(Rectangle button, IWin32Window owner)
    {
        var work = Screen.FromRectangle(button).WorkingArea;
        int gap = (int)Math.Round(6 * _scale);
        int x = button.Left - gap - Width;
        if (x < work.Left) x = button.Right + gap;
        int y = Math.Clamp(button.Top - Height / 2, work.Top, Math.Max(work.Top, work.Bottom - Height));
        Location = new Point(Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - Width)), y);
        Show(owner);
        Activate();
        _search.Focus();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        if (keyData == Keys.Enter && _grid.First is { } first) { _picked = true; Picked?.Invoke(first); Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(ToolBar.Border);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }

    /// <summary>One button per category, showing the category's first emoji.</summary>
    private sealed class CategoryButton : Control
    {
        private readonly string _emoji;
        private bool _hover;

        public CategoryButton(string emoji, string name, int cell)
        {
            _emoji = emoji;
            Size = new Size(cell, cell);
            Margin = new Padding(0, 0, 2, 0);
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
            BackColor = ToolBar.Background;
            new ToolTip().SetToolTip(this, name);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_hover)
            {
                using var fill = new SolidBrush(ToolBar.Hover);
                e.Graphics.FillRectangle(fill, ClientRectangle);
            }
            Grid.DrawEmoji(e.Graphics, _emoji, ClientRectangle, Width * 0.55f);
        }
    }

    /// <summary>The scrolling grid: section titles and emoji cells. Only what's in view is drawn.</summary>
    private sealed class Grid : Control
    {
        private readonly int _cell, _pad;
        private readonly Font _titleFont;
        private readonly List<(string Title, int Top)> _titles = [];
        private readonly List<(Rectangle Cell, string Emoji)> _cells = [];
        private int _hover = -1;

        public event Action<string>? Picked;

        public Grid(int cell, int pad, float scale)
        {
            _cell = cell;
            _pad = pad;
            _titleFont = new Font("Segoe UI Semibold", 11f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            DoubleBuffered = true;
            BackColor = ToolBar.Background;
            Cursor = Cursors.Hand;
        }

        public string? First => _cells.Count > 0 ? _cells[0].Emoji : null;

        public int SectionTop(string title) => _titles.FirstOrDefault(t => t.Title == title).Top;

        public void Show(List<(string Name, List<string> Items)> sections, int width)
        {
            _titles.Clear();
            _cells.Clear();
            _hover = -1;
            int y = _pad / 2, titleHeight = _titleFont.Height + _pad / 2;
            foreach (var (name, items) in sections)
            {
                _titles.Add((name, y));
                y += titleHeight;
                for (int i = 0; i < items.Count; i++)
                    _cells.Add((new Rectangle(_pad + i % Columns * _cell, y + i / Columns * _cell, _cell, _cell), items[i]));
                y += (items.Count + Columns - 1) / Columns * _cell + _pad / 2;
            }
            if (sections.Count == 1 && sections[0].Items.Count == 0) _titles[0] = ("No emoji found", _titles[0].Top);
            Size = new Size(width, y + _pad);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var clip = e.ClipRectangle;
            foreach (var (title, top) in _titles)
                if (top + _titleFont.Height >= clip.Top && top <= clip.Bottom)
                    TextRenderer.DrawText(g, title, _titleFont, new Point(_pad, top), Color.FromArgb(148, 163, 184), TextFormatFlags.NoPrefix);
            for (int i = 0; i < _cells.Count; i++)
            {
                var (cell, emoji) = _cells[i];
                if (!cell.IntersectsWith(clip)) continue;
                if (i == _hover)
                {
                    using var fill = new SolidBrush(ToolBar.Hover);
                    g.FillRectangle(fill, cell);
                }
                DrawEmoji(g, emoji, cell, _cell * 0.62f);
            }
        }

        /// <summary>Draws one emoji centred in <paramref name="cell"/>, at the size it was drawn (no resampling).</summary>
        internal static void DrawEmoji(Graphics g, string emoji, Rectangle cell, float size)
        {
            int px = (int)Math.Round(size);
            var picture = Pictures.GetOrAdd((emoji, px), k => EmojiRenderer.RenderUncached(k.Item1, k.Item2));
            g.DrawImage(picture, cell.X + (cell.Width - picture.Width) / 2, cell.Y + (cell.Height - picture.Height) / 2, picture.Width, picture.Height);
        }

        private int IndexAt(Point p) => _cells.FindIndex(c => c.Cell.Contains(p));

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = IndexAt(e.Location);
            if (i == _hover) return;
            if (_hover >= 0) Invalidate(_cells[_hover].Cell);
            _hover = i;
            if (i >= 0) Invalidate(_cells[i].Cell);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int i = IndexAt(e.Location);
            if (i >= 0 && e.Button == MouseButtons.Left) Picked?.Invoke(_cells[i].Emoji);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _titleFont.Dispose();
            base.Dispose(disposing);
        }
    }
}
