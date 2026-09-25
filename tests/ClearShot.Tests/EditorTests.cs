using System.Runtime.InteropServices;
using System.Drawing.Imaging;
using ClearShot.Editor;

namespace ClearShot.Tests;

public class AnnotationTests
{
    private static Bitmap Solid(int w, int h, Color c)
    {
        var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(b);
        g.Clear(c);
        return b;
    }

    private static bool IsRedish(Color c) => c.R > 150 && c.G < 90 && c.B < 90;

    [Fact]
    public void Arrow_has_a_head_and_a_plain_line_does_not()
    {
        using var doc = new EditDocument(Solid(200, 100, Color.Black));
        doc.Add(new LineShape { Color = Color.Red, Size = 3, Start = new(10, 50), End = new(180, 50), Arrow = true });
        // The head is wider than the shaft: a few pixels above the line near the tip are red.
        Assert.True(IsRedish(doc.Baked.GetPixel(170, 46)));
        Assert.False(IsRedish(doc.Baked.GetPixel(60, 45)));

        using var plain = new EditDocument(Solid(200, 100, Color.Black));
        plain.Add(new LineShape { Color = Color.Red, Size = 3, Start = new(10, 50), End = new(180, 50) });
        Assert.False(IsRedish(plain.Baked.GetPixel(170, 46)));
        Assert.True(IsRedish(plain.Baked.GetPixel(100, 50)));
    }

    [Fact]
    public void Pixelate_destroys_the_detail_underneath()
    {
        // Fine 1-pixel stripes, like small text.
        using var stripes = new Bitmap(120, 60, PixelFormat.Format32bppArgb);
        for (int y = 0; y < 60; y++)
        for (int x = 0; x < 120; x++)
            stripes.SetPixel(x, y, x % 2 == 0 ? Color.White : Color.Black);
        using var doc = new EditDocument(stripes);
        doc.Add(new PixelateBox { Start = new(0, 0), End = new(56, 56), BlockSize = 14 });

        // Inside every block all pixels are identical, so the stripes are gone.
        for (int by = 0; by < 56; by += 14)
        for (int bx = 0; bx < 56; bx += 14)
        {
            var first = doc.Baked.GetPixel(bx, by);
            for (int y = by; y < by + 14; y++)
            for (int x = bx; x < bx + 14; x++)
                Assert.Equal(first.ToArgb(), doc.Baked.GetPixel(x, y).ToArgb());
        }
        // Outside the box the stripes are untouched.
        Assert.Equal(Color.White.ToArgb(), doc.Baked.GetPixel(80, 30).ToArgb());
        Assert.Equal(Color.Black.ToArgb(), doc.Baked.GetPixel(81, 30).ToArgb());
    }

    [Fact]
    public void Eraser_brings_back_the_original_only_where_it_was_rubbed()
    {
        using var stripes = new Bitmap(120, 60, PixelFormat.Format32bppArgb);
        for (int y = 0; y < 60; y++)
        for (int x = 0; x < 120; x++)
            stripes.SetPixel(x, y, x % 2 == 0 ? Color.White : Color.Black);
        using var doc = new EditDocument(stripes);
        doc.Add(new PixelateBox { Start = new(0, 0), End = new(112, 56), BlockSize = 14 });
        var eraser = new EraserStroke { Size = 10, Original = doc.Original };
        eraser.Points.AddRange([new(0, 28), new(112, 28)]);
        doc.Add(eraser);

        // Under the eraser: the original stripes again.
        Assert.Equal(Color.White.ToArgb(), doc.Baked.GetPixel(40, 28).ToArgb());
        Assert.Equal(Color.Black.ToArgb(), doc.Baked.GetPixel(41, 28).ToArgb());
        // Away from it: still pixelated (neighbouring pixels identical).
        Assert.Equal(doc.Baked.GetPixel(40, 5).ToArgb(), doc.Baked.GetPixel(41, 5).ToArgb());
        Assert.Equal(doc.Baked.GetPixel(40, 50).ToArgb(), doc.Baked.GetPixel(41, 50).ToArgb());
        // Undo puts the pixelation back.
        doc.Undo();
        Assert.Equal(doc.Baked.GetPixel(40, 28).ToArgb(), doc.Baked.GetPixel(41, 28).ToArgb());
    }

    /// <summary>25/09: pressing and moving 0 px gave the eraser two identical points; GDI+'s Widen threw and the editor
    /// turned into a white box with a red X.</summary>
    [Theory]
    [InlineData(new float[] { 40, 28, 40, 28 })]
    [InlineData(new float[] { 40, 28, 40, 28, 40, 28 })]
    [InlineData(new float[] { 40, 28, 40, 28, 80, 28, 80, 28 })]
    public void Eraser_survives_repeated_points(float[] xy)
    {
        using var doc = new EditDocument(new Bitmap(120, 60, PixelFormat.Format32bppArgb));
        var eraser = new EraserStroke { Size = 10, Original = doc.Original };
        for (int i = 0; i < xy.Length; i += 2) eraser.Points.Add(new PointF(xy[i], xy[i + 1]));
        doc.Add(eraser); // draws it: must not throw
        using var g = Graphics.FromImage(doc.Baked);
        eraser.Draw(g, doc.Baked);
    }

    [Fact]
    public void Emoji_are_drawn_in_colour()
    {
        using var doc = new EditDocument(Solid(200, 200, Color.Gray));
        doc.Add(new EmojiSticker { Emoji = "\U0001F525", Size = 80, Centre = new(100, 100) }); // fire
        int coloured = 0;
        for (int y = 50; y < 150; y += 2)
        for (int x = 50; x < 150; x += 2)
        {
            var c = doc.Baked.GetPixel(x, y);
            if (Math.Abs(c.R - c.B) > 60) coloured++;
        }
        Assert.True(coloured > 100, $"Only {coloured} coloured pixels: the emoji came out black and white.");
    }

    [Fact]
    public void Steps_count_up_and_undo_takes_them_back()
    {
        using var doc = new EditDocument(Solid(300, 100, Color.Gray));
        Assert.Equal(1, doc.NextStepNumber);
        doc.Add(new StepMarker { Color = Color.Red, Size = 3, Centre = new(50, 50), Number = doc.NextStepNumber });
        doc.Add(new StepMarker { Color = Color.Red, Size = 3, Centre = new(150, 50), Number = doc.NextStepNumber });
        Assert.Equal(3, doc.NextStepNumber);
        Assert.Equal([1, 2], doc.Items.OfType<StepMarker>().Select(s => s.Number));
        doc.Undo();
        Assert.Equal(2, doc.NextStepNumber);
        Assert.True(IsRedish(doc.Baked.GetPixel(50 - 8, 50)));
        Assert.Equal(Color.Gray.ToArgb(), doc.Baked.GetPixel(150 - 8, 50).ToArgb());
    }

    [Fact]
    public void Undo_restores_the_original_pixels_exactly()
    {
        using var original = Solid(100, 100, Color.FromArgb(12, 34, 56));
        using var doc = new EditDocument(original);
        var stroke = new Stroke { Color = Color.Yellow, Size = 6 };
        stroke.Points.AddRange([new(10, 10), new(90, 90), new(10, 90)]);
        doc.Add(stroke);
        Assert.NotEqual(original.GetPixel(50, 50).ToArgb(), doc.Baked.GetPixel(50, 50).ToArgb());
        doc.Undo();
        for (int y = 0; y < 100; y += 3)
        for (int x = 0; x < 100; x += 3)
            Assert.Equal(original.GetPixel(x, y).ToArgb(), doc.Baked.GetPixel(x, y).ToArgb());
        Assert.True(doc.Undo().IsEmpty);
    }

    [Fact]
    public void Highlighter_does_not_get_darker_where_it_crosses_itself()
    {
        using var doc = new EditDocument(Solid(200, 200, Color.White));
        var mark = new Stroke { Color = Color.Blue, Size = 5, Highlighter = true };
        mark.Points.AddRange([new(20, 100), new(180, 100), new(180, 60), new(100, 60), new(100, 160)]);
        doc.Add(mark);
        var once = doc.Baked.GetPixel(50, 100);
        var crossing = doc.Baked.GetPixel(100, 100);
        Assert.InRange(Math.Abs(once.R - crossing.R), 0, 3);
        // And it's see-through: white shows through the blue.
        Assert.True(once.R > 80);
    }

    [Fact]
    public void Text_is_drawn()
    {
        using var doc = new EditDocument(Solid(300, 80, Color.Black));
        doc.Add(new TextNote { Color = Color.White, Size = 30, Origin = new(10, 10), Text = "Hello" });
        int lit = 0;
        for (int y = 0; y < 80; y++)
        for (int x = 0; x < 300; x++)
            if (doc.Baked.GetPixel(x, y).R > 128) lit++;
        Assert.InRange(lit, 100, 5000);
    }

    [Fact]
    public void Render_crops_the_final_area_at_full_resolution()
    {
        using var original = new Bitmap(100, 100, PixelFormat.Format32bppArgb);
        for (int y = 0; y < 100; y++)
        for (int x = 0; x < 100; x++)
            original.SetPixel(x, y, Color.FromArgb(x, y, 7));
        using var doc = new EditDocument(original);
        using var cut = doc.Render(new Rectangle(20, 30, 40, 25));
        Assert.Equal(new Size(40, 25), cut.Size);
        Assert.Equal(Color.FromArgb(20, 30, 7).ToArgb(), cut.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.FromArgb(59, 54, 7).ToArgb(), cut.GetPixel(39, 24).ToArgb());
    }
}

[Collection("Editor windows")]
public class EditorOverlayTests
{
    private static void OnUiThread(Action test)
    {
        Exception? failure = null;
        var t = new Thread(() =>
        {
            try { test(); }
            catch (Exception ex) { failure = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (failure is not null) throw failure;
    }

    private static void Pump()
    {
        for (int i = 0; i < 5; i++) { Application.DoEvents(); Thread.Sleep(10); }
    }

    // Far off every real screen, so the test never flashes anything in front of the user.
    private static readonly Rectangle Monitor = new(-30000, -30000, 800, 600);

    [Fact]
    public void Drawing_an_arrow_then_Enter_copies_and_saves_it()
    {
        OnUiThread(() =>
        {
            using var doc = new EditDocument(new Bitmap(800, 600));
            using var editor = new EditorOverlay(doc, Monitor, new Rectangle(100, 100, 300, 200)) { TakeFocus = false };
            var run = editor.RunAsync();
            Pump();
            editor.PickTool(Tool.Arrow);
            editor.PointerDown(new Point(Monitor.X + 150, Monitor.Y + 150), MouseButtons.Left);
            editor.PointerMove(new Point(Monitor.X + 300, Monitor.Y + 250), new Control());
            editor.PointerUp();
            Assert.Single(doc.Items.OfType<LineShape>());
            Assert.True(editor.Key(Keys.Enter));
            Pump();
            Assert.True(run.IsCompleted);
            Assert.Equal(EditAction.Done, run.Result.Action);
            Assert.Equal(new Rectangle(100, 100, 300, 200), run.Result.Area);
        });
    }

    [Fact]
    public void Dragging_the_right_edge_resizes_and_dragging_inside_moves()
    {
        OnUiThread(() =>
        {
            using var doc = new EditDocument(new Bitmap(800, 600));
            using var editor = new EditorOverlay(doc, Monitor, new Rectangle(100, 100, 300, 200)) { TakeFocus = false };
            var run = editor.RunAsync();
            Pump();
            var dummy = new Control();
            editor.PointerDown(new Point(Monitor.X + 400, Monitor.Y + 200), MouseButtons.Left);
            editor.PointerMove(new Point(Monitor.X + 450, Monitor.Y + 200), dummy);
            editor.PointerUp();
            Assert.Equal(new Rectangle(100, 100, 350, 200), editor.Area);

            // With no tool picked, dragging inside moves the whole area, never past the monitor's edge.
            editor.PointerDown(new Point(Monitor.X + 200, Monitor.Y + 200), MouseButtons.Left);
            editor.PointerMove(new Point(Monitor.X + 180, Monitor.Y + 900), dummy);
            editor.PointerUp();
            Assert.Equal(new Rectangle(80, 400, 350, 200), editor.Area);

            editor.Key(Keys.Escape);
            Assert.Equal(EditAction.Cancel, run.Result.Action);
        });
    }

    [Fact]
    public void Typing_text_keeps_letters_out_of_the_tool_shortcuts()
    {
        OnUiThread(() =>
        {
            using var doc = new EditDocument(new Bitmap(800, 600));
            using var editor = new EditorOverlay(doc, Monitor, new Rectangle(100, 100, 300, 200)) { TakeFocus = false };
            var run = editor.RunAsync();
            Pump();
            editor.PickTool(Tool.Text);
            editor.PointerDown(new Point(Monitor.X + 120, Monitor.Y + 120), MouseButtons.Left);
            // "p" would normally pick the pen; while typing it is just a letter.
            Assert.False(editor.Key(Keys.P));
            foreach (var c in "pal") editor.TypeChar(c);
            Assert.Equal(Tool.Text, editor.Tool);
            Assert.True(editor.Key(Keys.Escape)); // finishes the text, doesn't close
            Assert.False(run.IsCompleted);
            Assert.Equal("pal", doc.Items.OfType<TextNote>().Single().Text);

            Assert.True(editor.Key(Keys.Control | Keys.Z));
            Assert.Empty(doc.Items);
            editor.Key(Keys.Control | Keys.C);
            Assert.Equal(EditAction.Copy, run.Result.Action);
        });
    }

    [Fact]
    public void E_places_emoji_that_scroll_select_and_swap_like_other_drawings()
    {
        OnUiThread(() =>
        {
            using var doc = new EditDocument(new Bitmap(800, 600));
            using var editor = new EditorOverlay(doc, Monitor, new Rectangle(0, 0, 800, 600)) { TakeFocus = false };
            var run = editor.RunAsync();
            Pump();
            Assert.True(editor.Key(Keys.E));
            Assert.Equal(Tool.Emoji, editor.Tool);
            float size = editor.EmojiSize;
            editor.Wheel(120);
            Assert.True(editor.EmojiSize > size);
            editor.PointerDown(new Point(Monitor.X + 200, Monitor.Y + 200), MouseButtons.Left);
            editor.PointerUp();
            var sticker = doc.Items.OfType<EmojiSticker>().Single();
            Assert.Equal(editor.EmojiSize, sticker.Size);

            // Select it, make it bigger, swap it for another emoji, undo the swap.
            editor.PickTool(Tool.None);
            editor.PointerDown(new Point(Monitor.X + 200, Monitor.Y + 200), MouseButtons.Left);
            editor.PointerUp();
            Assert.Same(sticker, editor.Selected);
            editor.Wheel(120);
            Assert.True(sticker.Size > size);
            // Scrolling over an emoji resizes it with no selection, whatever the tool.
            editor.Key(Keys.Escape); // deselect
            editor.PickTool(Tool.Arrow);
            editor.PointerMove(new Point(Monitor.X + 200, Monitor.Y + 200), new Control());
            float before = sticker.Size;
            editor.Wheel(120);
            Assert.True(sticker.Size > before);
            editor.PickTool(Tool.None);
            editor.PointerDown(new Point(Monitor.X + 200, Monitor.Y + 200), MouseButtons.Left);
            editor.PointerUp();
            Assert.Same(sticker, editor.Selected);
            editor.PickEmoji(EmojiRenderer.Quick[16]);
            Assert.Equal(EmojiRenderer.Quick[16], doc.Items.OfType<EmojiSticker>().Single().Emoji);
            editor.Undo();
            Assert.Same(sticker, doc.Items.OfType<EmojiSticker>().Single());
            editor.Finish(EditAction.Cancel);
        });
    }

    [Fact]
    public void X_picks_the_eraser_and_select_looks_straight_through_it()
    {
        OnUiThread(() =>
        {
            using var doc = new EditDocument(new Bitmap(800, 600));
            using var editor = new EditorOverlay(doc, Monitor, new Rectangle(100, 100, 300, 200)) { TakeFocus = false };
            var run = editor.RunAsync();
            Pump();
            editor.PickTool(Tool.Pixelate);
            editor.PointerDown(new Point(Monitor.X + 120, Monitor.Y + 120), MouseButtons.Left);
            editor.PointerMove(new Point(Monitor.X + 300, Monitor.Y + 250), new Control());
            editor.PointerUp();
            Assert.True(editor.Key(Keys.X));
            Assert.Equal(Tool.Eraser, editor.Tool);
            editor.PointerDown(new Point(Monitor.X + 150, Monitor.Y + 180), MouseButtons.Left);
            editor.PointerMove(new Point(Monitor.X + 280, Monitor.Y + 180), new Control());
            editor.PointerUp();
            Assert.Single(doc.Items.OfType<EraserStroke>());
            float before = editor.EraserSize;
            editor.Wheel(120);
            Assert.True(editor.EraserSize > before);

            // Clicking on the erased line with Select picks the pixelate box underneath, not the eraser.
            editor.PickTool(Tool.None);
            editor.PointerDown(new Point(Monitor.X + 200, Monitor.Y + 180), MouseButtons.Left);
            editor.PointerUp();
            Assert.IsType<PixelateBox>(editor.Selected);
            editor.Finish(EditAction.Cancel);
        });
    }

    [Fact]
    public void Steps_number_themselves_as_you_click()
    {
        OnUiThread(() =>
        {
            using var doc = new EditDocument(new Bitmap(800, 600));
            using var editor = new EditorOverlay(doc, Monitor, new Rectangle(100, 100, 300, 200)) { TakeFocus = false };
            var run = editor.RunAsync();
            Pump();
            Assert.True(editor.Key(Keys.N));
            for (int i = 0; i < 3; i++)
            {
                editor.PointerDown(new Point(Monitor.X + 150 + i * 40, Monitor.Y + 150), MouseButtons.Left);
                editor.PointerUp();
            }
            Assert.Equal([1, 2, 3], doc.Items.OfType<StepMarker>().Select(s => s.Number));
            // Clicking outside the area adds nothing.
            editor.PointerDown(new Point(Monitor.X + 700, Monitor.Y + 500), MouseButtons.Left);
            editor.PointerUp();
            Assert.Equal(3, doc.Items.Count);
            editor.Finish(EditAction.Pin);
            Assert.Equal(EditAction.Pin, run.Result.Action);
        });
    }
}

public class EditShortcutSettingsTests
{
    [Fact]
    public void Edit_shortcut_defaults_and_survives_saving()
    {
        Assert.Equal("Alt+Shift+E", new Settings().EditHotkey);
        var path = Path.Combine(Path.GetTempPath(), $"clearshot-test-{Guid.NewGuid():N}.json");
        try
        {
            new Settings { EditHotkey = "Ctrl+Alt+E" }.Save(path);
            Assert.Equal("Ctrl+Alt+E", Settings.Load(path).EditHotkey);
            // Settings saved by older versions have no edit shortcut: they get the default.
            File.WriteAllText(path, "{ \"RegionHotkey\": \"Alt+Shift+C\" }");
            Assert.Equal("Alt+Shift+E", Settings.Load(path).EditHotkey);
        }
        finally
        {
            File.Delete(path);
        }
        Assert.True(Hotkey.TryParse("Alt+Shift+E", out var hk) && hk.IsSafeAsGlobalShortcut);
    }
}

[Collection("Editor windows")]
public class EditorKeyboardTests
{
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

    /// <summary>The bug from 25/09: after typing text the editor seemed frozen. Real key messages must get you out.</summary>
    [Fact]
    public void Real_key_presses_type_text_and_escape_gets_out()
    {
        Exception? failure = null;
        var t = new Thread(() =>
        {
            try
            {
                var monitor = new Rectangle(-30000, -30000, 800, 600);
                using var doc = new EditDocument(new Bitmap(800, 600));
                using var editor = new EditorOverlay(doc, monitor, new Rectangle(100, 100, 300, 200)) { TakeFocus = false };
                var run = editor.RunAsync();
                var canvas = Application.OpenForms.Cast<Form>().ToList().Single(f => f.Text == "ClearShot editor");
                void Pump() { for (int i = 0; i < 10; i++) { Application.DoEvents(); Thread.Sleep(10); } }
                void Press(Keys k, char? ch = null)
                {
                    PostMessage(canvas.Handle, 0x100, (IntPtr)(int)k, IntPtr.Zero);
                    // The message loop turns the key-down into the typed character itself.
                    Pump();
                }
                Pump();
                editor.PickTool(Tool.Text);
                editor.PointerDown(new Point(monitor.X + 120, monitor.Y + 120), MouseButtons.Left);
                Press(Keys.H, 'h');
                Press(Keys.I, 'i');
                Press(Keys.Escape);
                Assert.Equal("hi", doc.Items.OfType<TextNote>().Single().Text);
                Press(Keys.Escape);
                Assert.True(run.IsCompleted);
                Assert.Equal(EditAction.Cancel, run.Result.Action);
            }
            catch (Exception ex) { failure = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (failure is not null) throw failure;
    }
}

[Collection("Editor windows")]
public class TextEditingTests
{
    private static readonly Rectangle Monitor = new(-30000, -30000, 800, 600);

    private static void OnUiThread(Action test)
    {
        Exception? failure = null;
        var t = new Thread(() => { try { test(); } catch (Exception ex) { failure = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (failure is not null) throw failure;
    }

    private static Point S(float x, float y) => new(Monitor.X + (int)x, Monitor.Y + (int)y);

    [Fact]
    public void Dragging_the_corner_makes_text_any_size_and_dragging_the_text_moves_it()
    {
        OnUiThread(() =>
        {
            using var doc = new EditDocument(new Bitmap(800, 600));
            using var editor = new EditorOverlay(doc, Monitor, new Rectangle(0, 0, 800, 600)) { TakeFocus = false };
            var run = editor.RunAsync();
            Application.DoEvents();
            editor.PickTool(Tool.Text);
            editor.PointerDown(S(100, 100), MouseButtons.Left);
            foreach (var c in "Big") editor.TypeChar(c);
            var note = (TextNote)typeof(EditorOverlay).GetField("_typing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(editor)!;
            float startSize = note.Size;
            var box = note.Measure(out _);

            // Corner square is at the bottom-right of the dotted box (3 px right, 2 px below the text).
            var corner = new PointF(box.Right + 3, box.Bottom + 2);
            editor.PointerDown(S(corner.X, corner.Y), MouseButtons.Left);
            editor.PointerMove(S(corner.X + box.Width * 3, corner.Y + box.Height * 3), new Control());
            editor.PointerUp();
            Assert.InRange(note.Size, startSize * 3.5f, startSize * 4.5f);
            Assert.Equal(note.Size, editor.TextSize); // the next text starts at this size too

            // Drag the text body to move it.
            editor.PointerDown(S(box.X + 5, box.Y + 5), MouseButtons.Left);
            editor.PointerMove(S(box.X + 55, box.Y + 45), new Control());
            editor.PointerUp();
            Assert.Equal(new PointF(150, 140), note.Origin);

            // Fonts apply to the text being typed.
            editor.SetFont("Georgia", bold: false);
            Assert.Equal("Georgia", note.FontName);
            Assert.False(note.Bold);

            // Finish, then click it again with the text tool to carry on editing.
            editor.Key(Keys.Escape);
            Assert.Single(doc.Items);
            editor.PointerDown(S(160, 150), MouseButtons.Left);
            editor.PointerUp();
            Assert.Empty(doc.Items);
            editor.TypeChar('!');
            editor.Key(Keys.Escape);
            Assert.Equal("Big!", doc.Items.OfType<TextNote>().Single().Text);

            // Right-click outside closes.
            editor.PointerDown(S(700, 10), MouseButtons.Right);
            Assert.Equal(EditAction.Cancel, run.Result.Action);
        });
    }

    [Fact]
    public void One_colour_box_remembers_per_tool_and_recolours_what_you_just_drew()
    {
        OnUiThread(() =>
        {
            using var doc = new EditDocument(new Bitmap(800, 600));
            using var editor = new EditorOverlay(doc, Monitor, new Rectangle(0, 0, 800, 600)) { TakeFocus = false };
            var run = editor.RunAsync();
            Application.DoEvents();
            var blue = EditorOverlay.Palette[4].Color;
            var green = EditorOverlay.Palette[3].Color;

            editor.PickTool(Tool.Highlighter);
            Assert.Equal(EditorOverlay.Palette[2].Color, editor.Colour); // highlighter starts yellow

            editor.PickTool(Tool.Arrow);
            editor.PointerDown(S(100, 100), MouseButtons.Left);
            editor.PointerMove(S(300, 200), new Control());
            editor.PointerUp();
            var arrow = doc.Items.OfType<LineShape>().Single();
            editor.SetColour(blue);
            Assert.Equal(blue.ToArgb(), arrow.Color.ToArgb()); // the arrow just drawn changes
            Assert.True(doc.Baked.GetPixel(200, 150).B > 150);

            // Another tool: picking a colour there leaves the arrow alone, and each tool keeps its own.
            editor.PickTool(Tool.Rectangle);
            editor.SetColour(green);
            Assert.Equal(blue.ToArgb(), arrow.Color.ToArgb());
            editor.PickTool(Tool.Arrow);
            Assert.Equal(blue.ToArgb(), editor.Colour.ToArgb());
            editor.PickTool(Tool.Rectangle);
            Assert.Equal(green.ToArgb(), editor.Colour.ToArgb());
            editor.Finish(EditAction.Cancel);
        });
    }

    [Fact]
    public void Select_tool_moves_recolours_resizes_and_deletes_old_drawings_and_undo_reverses_each()
    {
        OnUiThread(() =>
        {
            using var doc = new EditDocument(new Bitmap(800, 600));
            using var editor = new EditorOverlay(doc, Monitor, new Rectangle(0, 0, 800, 600)) { TakeFocus = false };
            var run = editor.RunAsync();
            Application.DoEvents();
            var dummy = new Control();
            void Drag(int x1, int y1, int x2, int y2)
            {
                editor.PointerDown(S(x1, y1), MouseButtons.Left);
                editor.PointerMove(S(x2, y2), dummy);
                editor.PointerUp();
            }

            editor.PickTool(Tool.Rectangle);
            Drag(100, 100, 200, 200);
            editor.PickTool(Tool.Arrow);
            Drag(300, 300, 400, 300);
            var box = doc.Items.OfType<RectangleShape>().Single();
            var arrow = doc.Items.OfType<LineShape>().Single();

            // Select (V), click the older rectangle's edge and drag it.
            Assert.True(editor.Key(Keys.V));
            Drag(100, 150, 130, 170);
            Assert.Same(box, editor.Selected);
            Assert.Equal(new PointF(130, 120), box.Start);
            Assert.Equal(2, doc.Items.Count);
            Assert.Same(box, doc.Items[0]); // stays underneath the arrow

            // Recolour and resize the selected drawing only.
            var green = EditorOverlay.Palette[3].Color;
            var arrowColour = arrow.Color;
            editor.SetColour(green);
            Assert.Equal(green.ToArgb(), box.Color.ToArgb());
            Assert.Equal(arrowColour.ToArgb(), arrow.Color.ToArgb());
            float size = box.Size;
            editor.Wheel(120);
            Assert.Equal(size + 1, box.Size);

            // Delete it, then undo everything step by step.
            Assert.True(editor.Key(Keys.Delete));
            Assert.Single(doc.Items);
            Assert.Null(editor.Selected);
            editor.Undo();
            Assert.Same(box, doc.Items[0]);
            editor.Undo();
            Assert.Equal(size, box.Size);
            editor.Undo();
            Assert.NotEqual(green.ToArgb(), box.Color.ToArgb());
            editor.Undo();
            Assert.Equal(new PointF(100, 100), box.Start);

            // Clicking the middle of the empty rectangle selects nothing and moves the area instead.
            Drag(150, 150, 150, 150);
            Assert.Null(editor.Selected);

            // Esc first deselects, then closes.
            Drag(350, 300, 350, 300);
            Assert.Same(arrow, editor.Selected);
            editor.Key(Keys.Escape);
            Assert.False(run.IsCompleted);
            editor.Key(Keys.Escape);
            Assert.Equal(EditAction.Cancel, run.Result.Action);
        });
    }

    [Fact]
    public void Dragging_an_arrow_end_makes_it_longer_and_a_rectangle_corner_resizes_it()
    {
        OnUiThread(() =>
        {
            using var doc = new EditDocument(new Bitmap(800, 600));
            using var editor = new EditorOverlay(doc, Monitor, new Rectangle(0, 0, 800, 600)) { TakeFocus = false };
            var run = editor.RunAsync();
            Application.DoEvents();
            var dummy = new Control();
            void Drag(int x1, int y1, int x2, int y2)
            {
                editor.PointerDown(S(x1, y1), MouseButtons.Left);
                editor.PointerMove(S(x2, y2), dummy);
                editor.PointerUp();
            }
            editor.PickTool(Tool.Arrow);
            Drag(100, 100, 200, 100);
            editor.PickTool(Tool.Rectangle);
            Drag(300, 300, 400, 400);
            var arrow = doc.Items.OfType<LineShape>().Single();
            var box = doc.Items.OfType<RectangleShape>().Single();

            editor.PickTool(Tool.None);
            Drag(150, 100, 150, 100); // select the arrow
            Drag(200, 100, 500, 250); // drag its tip
            Assert.Equal(new PointF(100, 100), arrow.Start);
            Assert.Equal(new PointF(500, 250), arrow.End);
            Assert.Same(arrow, doc.Items[0]);

            Drag(300, 350, 300, 350); // select the rectangle by its edge
            Assert.Same(box, editor.Selected);
            Drag(400, 300, 450, 250); // top-right corner out
            Assert.Equal(new PointF(300, 250), box.Start);
            Assert.Equal(new PointF(450, 400), box.End);

            editor.Undo();
            Assert.Equal((new PointF(300, 300), new PointF(400, 400)), (box.Start, box.End));
            editor.Undo();
            Assert.Equal(new PointF(200, 100), arrow.End);
            editor.Finish(EditAction.Cancel);
        });
    }

    [Fact]
    public void Missing_font_falls_back_instead_of_failing()
    {
        var note = new TextNote { Text = "x", Size = 20, FontName = "No Such Font 123" };
        using var font = note.MakeFont();
        Assert.NotNull(font);
    }
}

public class HelpFormTests
{
    [Fact]
    public void Help_shows_the_users_own_shortcuts_and_every_tool_key()
    {
        Exception? failure = null;
        var t = new Thread(() =>
        {
            try
            {
                using var form = new HelpForm(new Settings { RegionHotkey = "Ctrl+Shift+C" });
                static IEnumerable<Control> All(Control c) => c.Controls.Cast<Control>().SelectMany(x => new[] { x }.Concat(All(x)));
                var texts = All(form).OfType<Label>().Select(l => l.Text).ToList();
                Assert.Contains(texts, s => s.Contains("Ctrl") && s.Contains("Shift") && s.Contains('C') && !s.Contains("Alt"));
                foreach (var key in new[] { "V", "P", "R", "H", "T", "N", "B", "Enter", "Esc" })
                    Assert.Contains(key, texts);
                // Reachable: a "How to use" link in the main window.
                using var settingsForm = new SettingsForm(new Settings());
                Assert.Contains(All(settingsForm).OfType<LinkLabel>(), l => l.Text == "How to use");
                // The footer links sit on one line (25/09: "How to use" was 3 px lower than "Buy me a beer").
                settingsForm.CreateControl();
                settingsForm.PerformLayout();
                var tops = All(settingsForm).OfType<LinkLabel>().Select(l => l.Top).Distinct().ToList();
                Assert.Single(tops);
            }
            catch (Exception ex) { failure = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (failure is not null) throw failure;
    }
}

public class EmojiCatalogTests
{
    [Fact]
    public void Full_standard_set_loads_grouped_and_searchable_without_flags_windows_cannot_draw()
    {
        var all = EmojiCatalog.All;
        Assert.InRange(all.Count, 1500, 2000);
        Assert.Contains(all, e => e.Emoji == "\U0001F525" && e.Name == "fire");
        Assert.Contains(all, e => e.Emoji == "\U0001F3C1"); // chequered flag: Windows draws it
        Assert.DoesNotContain(all, e => e.Name == "flag: United Kingdom"); // country flags show as letters on Windows
        Assert.Equal(9, all.Select(e => e.Group).Distinct().Count());
        Assert.Contains(EmojiCatalog.Search("thumbs"), e => e.Emoji == "\U0001F44D");
        Assert.Contains(EmojiCatalog.Search("heart red"), e => e.Name == "red heart");
        Assert.Empty(EmojiCatalog.Search("zzqqxx"));
    }
}

public class EmojiDrawableTests
{
    [Fact]
    public void Single_emoji_draw_as_one_and_the_picker_hides_combinations_windows_splits()
    {
        Assert.True(EmojiRenderer.DrawsAsOne("\U0001F525"));             // fire
        Assert.True(EmojiRenderer.DrawsAsOne("❤️"));           // red heart
        Assert.True(EmojiRenderer.DrawsAsOne("\U0001F3F3️‍\U0001F308")); // rainbow flag (a combination Windows has)
        Assert.False(EmojiRenderer.DrawsAsOne("\U0001F525\U0001F525"));  // two emoji side by side
        // "couple with heart: woman, man": Windows shows two faces and a heart squeezed together, not one picture.
        Assert.False(EmojiRenderer.DrawsAsOne("\U0001F469\u200D\u2764\uFE0F\u200D\U0001F468"));
        Assert.All(EmojiCatalog.All, e => Assert.True(EmojiRenderer.DrawsAsOne(e.Emoji), e.Name));
    }
}
