namespace ClearShot.Tests;

/// <summary>Renders the main window off-screen to a PNG for a visual check. Only runs with CLEARSHOT_RENDER set to a path.</summary>
public class WindowRenderTests
{
    [Fact]
    public void Render_main_window()
    {
        var output = Environment.GetEnvironmentVariable("CLEARSHOT_RENDER");
        if (string.IsNullOrEmpty(output)) return;

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                using var form = new SettingsForm(new Settings());
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-20000, -20000);
                form.ShowInTaskbar = false;
                form.Show();
                Application.DoEvents();
                using var bmp = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.Size));
                bmp.Save(output);
                form.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    /// <summary>Renders the donate panel with obviously fake addresses. Only runs with CLEARSHOT_RENDER_DONATE set to a path.</summary>
    [Fact]
    public void Render_donate_window()
    {
        var output = Environment.GetEnvironmentVariable("CLEARSHOT_RENDER_DONATE");
        if (string.IsNullOrEmpty(output)) return;

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                using var form = new DonateForm(
                [
                    new("Bitcoin", "bc1qexampleexampleexampleexampleexample00", "BTC only."),
                    new("Ethereum and more", "0x0000000000000000000000000000000000000000", "ETH, plus Base and more."),
                ]);
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-20000, -20000);
                form.ShowInTaskbar = false;
                form.Show();
                Application.DoEvents();
                using var bmp = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.Size));
                bmp.Save(output);
                form.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }
}
