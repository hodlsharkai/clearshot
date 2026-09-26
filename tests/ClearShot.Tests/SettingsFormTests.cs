namespace ClearShot.Tests;

public class SettingsFormTests
{
    /// <summary>Switching appearance rebuilds the window from a draft; nothing typed in the old window may be lost.</summary>
    [Fact]
    public void Draft_survives_a_window_rebuild()
    {
        Exception? failure = null;
        var t = new Thread(() =>
        {
            try
            {
                var saved = new Settings();
                var draftValues = new Settings
                {
                    SaveFolder = @"D:\Shots",
                    FullScreenHotkey = "Ctrl+Shift+F9",
                    RegionHotkey = "Alt+R",
                    PlaySound = false,
                    ShowPreview = false,
                    PauseMediaWhileSelecting = false,
                    FreezeWhileSelecting = true,
                    SaveHdrJxr = true,
                    SaveHdrPng = true,
                };
                using var form = new SettingsForm(saved, new SettingsForm.Draft(draftValues, StartWithWindows: true));
                var round = form.CaptureDraft();
                Assert.Equal(@"D:\Shots", round.Values.SaveFolder);
                Assert.Equal("Ctrl+Shift+F9", round.Values.FullScreenHotkey);
                Assert.Equal("Alt+R", round.Values.RegionHotkey);
                Assert.False(round.Values.PlaySound);
                Assert.False(round.Values.ShowPreview);
                Assert.False(round.Values.PauseMediaWhileSelecting);
                Assert.True(round.Values.FreezeWhileSelecting);
                Assert.True(round.Values.SaveHdrJxr);
                Assert.True(round.Values.SaveHdrPng);
                Assert.True(round.StartWithWindows);
                // The saved settings object is untouched until Save is pressed.
                Assert.Equal("Alt+C", saved.FullScreenHotkey);
            }
            catch (Exception ex) { failure = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (failure is not null) throw failure;
    }
}

public class SettingsFormAutoSaveTests
{
    private static IEnumerable<Control> FindAll(Control root) =>
        root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(FindAll(c)));

    /// <summary>The bug from 24/09: unticking "Pause videos" did nothing unless Save was pressed.</summary>
    [Fact]
    public void Unticking_pause_videos_is_saved_immediately()
    {
        Exception? failure = null;
        var t = new Thread(() =>
        {
            try
            {
                var settings = new Settings { PauseMediaWhileSelecting = true };
                using var form = new SettingsForm(settings);
                int changes = 0;
                form.SettingsChanged += () => changes++;
                var pause = FindAll(form).OfType<CheckBox>().First(c => c.Text.StartsWith("Pause videos"));
                pause.Checked = false;
                Assert.False(settings.PauseMediaWhileSelecting);
                Assert.Equal(1, changes);
                Assert.DoesNotContain(FindAll(form).OfType<Button>(), b => b.Text == "Save");
            }
            catch (Exception ex) { failure = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (failure is not null) throw failure;
    }
}

[Collection("Editor windows")] // real windows shown on screen take turns
public class SettingsTabsTests
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    /// <summary>25/09: the window changed height as you switched tabs. It must keep one size.</summary>
    [Fact]
    public void Window_keeps_one_size_across_all_tabs()
    {
        // As the app runs: per-monitor DPI aware, so the window is scaled for the display (this is where it went wrong).
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        Exception? failure = null;
        var t = new Thread(() =>
        {
            try
            {
                static IEnumerable<Control> All(Control c) => c.Controls.Cast<Control>().SelectMany(x => new[] { x }.Concat(All(x)));
                {
                    using var form = new SettingsForm(new Settings()) { StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000) };
                    form.Show();
                    Application.DoEvents();
                    var tabs = All(form).OfType<RadioButton>().Where(r => r.Appearance == Appearance.Button).ToList();
                    Assert.Equal(5, tabs.Count);
                    var sizes = new HashSet<Size>();
                    foreach (var tab in tabs.Concat(tabs))
                    {
                        tab.Checked = true;
                        Application.DoEvents();
                        form.PerformLayout();
                        sizes.Add(form.Size);
                    }
                    Assert.Single(sizes);
                    form.Close();
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (failure is not null) throw failure;
    }
}
