using ClearShot.Editor;

namespace ClearShot.Tests;

/// <summary>Renders the real editor windows (off-screen) into one picture, to judge the look. Set CLEARSHOT_EDITOR_LOOK=out.png;background.png</summary>
[Collection("Editor windows")]
public class EditorLookTests
{
    [Fact]
    public void Render_editor_look()
    {
        var spec = Environment.GetEnvironmentVariable("CLEARSHOT_EDITOR_LOOK");
        if (string.IsNullOrEmpty(spec)) return;
        var parts = spec.Split(';');
        Exception? failure = null;
        var t = new Thread(() =>
        {
            try
            {
                using var background = new Bitmap(parts[1]);
                var monitor = new Rectangle(-30000, -30000, background.Width, background.Height);
                using var doc = new EditDocument(new Bitmap(background));
                var area = new Rectangle(40, 100, 700, 420);
                using var editor = new EditorOverlay(doc, monitor, area) { TakeFocus = false };
                _ = editor.RunAsync();
                Application.DoEvents();
                editor.PickTool(Tool.Arrow);
                void Drag(int x1, int y1, int x2, int y2)
                {
                    editor.PointerDown(new Point(monitor.X + x1, monitor.Y + y1), MouseButtons.Left);
                    editor.PointerMove(new Point(monitor.X + x2, monitor.Y + y2), new Control());
                    editor.PointerUp();
                }
                Drag(450, 170, 330, 230);
                editor.PickTool(Tool.Rectangle);
                Drag(88, 385, 520, 520);
                editor.PickTool(Tool.Pixelate);
                Drag(100, 160, 190, 180);
                editor.PickTool(Tool.Step);
                Drag(560, 420, 560, 420);
                Drag(560, 470, 560, 470);
                editor.PickTool(Tool.Highlighter);
                Drag(110, 435, 180, 435);
                editor.PickTool(Tool.Text);
                editor.SetFont("Impact", false);
                editor.PointerDown(new Point(monitor.X + 470, monitor.Y + 140), MouseButtons.Left);
                foreach (var c in "Look here") editor.TypeChar(c);
                for (int i = 0; i < 10; i++) { Application.DoEvents(); Thread.Sleep(20); }

                using var output = new Bitmap(background.Width, background.Height);
                using (var g = Graphics.FromImage(output))
                {
                    g.DrawImage(background, 0, 0);
                    using var dim = new SolidBrush(Color.FromArgb(89, 0, 0, 0));
                    g.FillRectangle(dim, 0, 0, output.Width, output.Height);
                    foreach (Form f in Application.OpenForms.Cast<Form>().Where(f => f.Visible && f is not LiveRegionSelector.DimLayer).ToList())
                    {
                        using var shot = new Bitmap(f.Width, f.Height);
                        f.DrawToBitmap(shot, new Rectangle(Point.Empty, f.Size));
                        var at = new Point(f.Left - monitor.X, f.Top - monitor.Y);
                        var clip = f.Region is null ? null : f.Region.Clone();
                        if (clip is not null) { clip.Translate(at.X, at.Y); g.SetClip(clip, System.Drawing.Drawing2D.CombineMode.Replace); }
                        g.DrawImage(shot, at);
                        g.ResetClip();
                    }
                }
                output.Save(parts[0]);
                editor.Finish(EditAction.Cancel);
            }
            catch (Exception ex) { failure = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (failure is not null) throw failure;
    }
}
