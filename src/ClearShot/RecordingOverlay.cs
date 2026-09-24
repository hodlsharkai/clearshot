namespace ClearShot;

/// <summary>
/// While a GIF records: a red outline just outside the area (so it never appears in the GIF) and a small
/// timer with a Stop button beside it. Neither window takes focus, so games and players keep running.
/// </summary>
internal sealed class RecordingOverlay : IDisposable
{
    private const int WsExNoActivate = 0x08000000, WsExToolWindow = 0x80, WsExTopmost = 0x8, WsExTransparent = 0x20, WsExLayered = 0x80000;
    private static readonly Color Red = Color.FromArgb(239, 68, 68);
    private static readonly Color Key = Color.FromArgb(255, 0, 255);
    private const int Ring = 3;

    private readonly Outline _outline;
    private readonly Pill? _pill;
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 200 };
    private readonly DateTime _started = DateTime.Now;
    private readonly TimeSpan _limit;

    public event Action? StopClicked;

    /// <param name="area">The recorded area in screen coordinates.</param>
    public RecordingOverlay(Rectangle area, Rectangle monitorBounds, TimeSpan limit)
    {
        _limit = limit;
        var ring = area;
        ring.Inflate(Ring, Ring);
        _outline = new Outline(ring);

        float scale = Dpi.ScaleFor(monitorBounds);
        var pillSize = new Size((int)(250 * scale), (int)(34 * scale));
        // Beside the area, never on it: below, else above. If the area fills the screen there's no room, so the
        // shortcut or Esc is the way to stop.
        var below = new Rectangle(area.Right - pillSize.Width, ring.Bottom + 6, pillSize.Width, pillSize.Height);
        var above = below with { Y = ring.Top - 6 - pillSize.Height };
        Rectangle? spot = monitorBounds.Contains(below) ? below : monitorBounds.Contains(above) ? above : null;
        if (spot is Rectangle place)
        {
            place.X = Math.Clamp(place.X, monitorBounds.Left, monitorBounds.Right - place.Width);
            _pill = new Pill(place, scale);
            _pill.StopClicked += () => StopClicked?.Invoke();
        }
        _tick.Tick += (_, _) => _pill?.SetElapsed(DateTime.Now - _started, _limit);
    }

    public void Show()
    {
        _outline.Show();
        _pill?.Show();
        _pill?.SetElapsed(TimeSpan.Zero, _limit);
        _tick.Start();
    }

    public void Dispose()
    {
        _tick.Dispose();
        _outline.Dispose();
        _pill?.Dispose();
    }

    private sealed class Outline : Form
    {
        public Outline(Rectangle bounds)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Key;
            TransparencyKey = Key;
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

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Bounds = Bounds; // pin after DPI adjustments
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Key);
            using var brush = new SolidBrush(Red);
            e.Graphics.FillRectangle(brush, 0, 0, Width, Ring);
            e.Graphics.FillRectangle(brush, 0, Height - Ring, Width, Ring);
            e.Graphics.FillRectangle(brush, 0, 0, Ring, Height);
            e.Graphics.FillRectangle(brush, Width - Ring, 0, Ring, Height);
        }
    }

    private sealed class Pill : Form
    {
        private readonly Label _time;
        private readonly Label _stop;

        public event Action? StopClicked;

        public Pill(Rectangle bounds, float scale)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(15, 23, 42);
            var font = new Font("Segoe UI", 12f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            _time = new Label { ForeColor = Color.White, Font = font, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill, Padding = new Padding((int)(10 * scale), 0, 0, 0) };
            _stop = new Label
            {
                Text = "■  Stop",
                ForeColor = Color.White,
                BackColor = Red,
                Font = font,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Right,
                Width = (int)(80 * scale),
                Cursor = Cursors.Hand,
            };
            _stop.Click += (_, _) => StopClicked?.Invoke();
            Controls.Add(_time);
            Controls.Add(_stop);
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

        public void SetElapsed(TimeSpan elapsed, TimeSpan limit) =>
            _time.Text = $"●  REC  {elapsed:m\\:ss} / {limit:m\\:ss}";
    }
}
