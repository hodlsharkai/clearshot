namespace ClearShot.Tests;

/// <summary>Renders the settings window on a chosen tab, to judge the look. Set CLEARSHOT_SETTINGS_LOOK=out.png;TabName</summary>
public class SettingsLookTests
{
    [Fact]
    public void Render_settings_tab()
    {
        var spec = Environment.GetEnvironmentVariable("CLEARSHOT_SETTINGS_LOOK");
        if (string.IsNullOrEmpty(spec)) return;
        var parts = spec.Split(';');
        Exception? failure = null;
        var t = new Thread(() =>
        {
            try
            {
                static IEnumerable<Control> All(Control c) => c.Controls.Cast<Control>().SelectMany(x => new[] { x }.Concat(All(x)));
                using var form = new SettingsForm(new Settings()) { StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000) };
                form.Show();
                All(form).OfType<RadioButton>().First(r => r.Text == parts[1]).Checked = true;
                for (int i = 0; i < 5; i++) { Application.DoEvents(); Thread.Sleep(20); }
                using var bmp = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.Size));
                bmp.Save(parts[0]);
                form.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (failure is not null) throw failure;
    }
}
