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
                var settings = new Settings();
                Assert.True(settings.PauseMediaWhileSelecting);
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
