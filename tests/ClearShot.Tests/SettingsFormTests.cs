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
