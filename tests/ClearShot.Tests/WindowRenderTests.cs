namespace ClearShot.Tests;

/// <summary>
/// Draws the windows at the primary monitor's real scaling, in light and dark, for a visual check.
/// Only runs with CLEARSHOT_RENDER set to a folder. The windows are fully transparent while drawn.
/// </summary>
public class WindowRenderTests
{
    [Fact]
    public void Render_windows_light_and_dark()
    {
        var dir = Environment.GetEnvironmentVariable("CLEARSHOT_RENDER");
        if (string.IsNullOrEmpty(dir)) return;
        Exception? failure = null;
        var t = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                foreach (var theme in new[] { "Light", "Dark" })
                {
                    Theme.Apply(theme);
                    Render(new SettingsForm(new Settings()), Path.Combine(dir, $"main-{theme}.png"));
                    Render(new DonateForm(AppInfo.ActiveDonations), Path.Combine(dir, $"donate-{theme}.png"));
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (failure is not null) throw failure;
    }

    static void Render(Form form, string path)
    {
        using (form)
        {
            // On the real primary monitor (125%), fully transparent, so the true scaling applies.
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(200, 200);
            form.Opacity = 0;
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();
            Console.WriteLine($"REAL: {Path.GetFileName(path)} size {form.Size} client {form.ClientSize} dpi {form.DeviceDpi} preferred {form.PreferredSize}");
            using var bmp = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.Size));
            bmp.Save(path);
            form.Close();
        }
    }
}
