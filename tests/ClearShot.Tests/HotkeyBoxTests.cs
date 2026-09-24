namespace ClearShot.Tests;

public class HotkeyBoxTests
{
    private static void OnSta(Action<HotkeyBox> test)
    {
        Exception? failure = null;
        var t = new Thread(() =>
        {
            try
            {
                using var box = new HotkeyBox();
                Assert.True(Hotkey.TryParse("Alt+C", out var start));
                box.Value = start;
                test(box);
            }
            catch (Exception ex) { failure = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (failure is not null) throw failure;
    }

    [Fact]
    public void Held_together_records_all_three_keys() => OnSta(box =>
    {
        box.HandleKeyDown(Keys.Menu, Keys.Alt, false);
        box.HandleKeyDown(Keys.ShiftKey, Keys.Alt | Keys.Shift, false);
        box.HandleKeyDown(Keys.C, Keys.Alt | Keys.Shift, false);
        Assert.Equal("Alt+Shift+C", box.Value.ToString());
    });

    [Fact]
    public void Tapped_one_after_another_records_all_three_keys() => OnSta(box =>
    {
        box.HandleKeyDown(Keys.Menu, Keys.None, false);
        Assert.Equal("Alt + …", box.Text);
        box.HandleKeyDown(Keys.ShiftKey, Keys.None, false);
        Assert.Equal("Alt + Shift + …", box.Text);
        box.HandleKeyDown(Keys.C, Keys.None, false);
        Assert.Equal("Alt+Shift+C", box.Value.ToString());
    });

    [Fact]
    public void A_lone_letter_is_refused_and_the_old_shortcut_kept() => OnSta(box =>
    {
        box.HandleKeyDown(Keys.C, Keys.None, false);
        Assert.Equal("Alt+C", box.Value.ToString());
        Assert.Contains("Add Ctrl, Alt or Win", box.Text);
    });

    [Fact]
    public void Shift_plus_letter_is_refused() => OnSta(box =>
    {
        box.HandleKeyDown(Keys.S, Keys.Shift, false);
        Assert.Equal("Alt+C", box.Value.ToString());
    });

    [Fact]
    public void Function_keys_and_print_screen_work_alone() => OnSta(box =>
    {
        box.HandleKeyDown(Keys.F9, Keys.None, false);
        Assert.Equal("F9", box.Value.ToString());
        box.HandleKeyDown(Keys.PrintScreen, Keys.None, false);
        Assert.Equal("PrintScreen", box.Value.ToString());
    });

    [Fact]
    public void Win_key_combinations_record() => OnSta(box =>
    {
        box.HandleKeyDown(Keys.S, Keys.Shift, winHeld: true);
        Assert.Equal("Shift+Win+S", box.Value.ToString());
    });
}
