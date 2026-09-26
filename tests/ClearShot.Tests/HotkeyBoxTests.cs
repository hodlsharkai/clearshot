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

    /// <summary>26/09: any key is allowed on its own, with no warning: it's the user's choice (Page Down, a letter...).</summary>
    [Fact]
    public void Any_key_alone_is_accepted_without_a_warning() => OnSta(box =>
    {
        box.HandleKeyDown(Keys.PageDown, Keys.None, false);
        Assert.Equal("PageDown", box.Value.ToString());
        box.HandleKeyDown(Keys.C, Keys.None, false);
        Assert.Equal("C", box.Value.ToString());
        box.HandleKeyDown(Keys.S, Keys.Shift, false);
        Assert.Equal("Shift+S", box.Value.ToString());
        Assert.DoesNotContain("would fire", box.Text);
    });

    [Fact]
    public void Mouse_buttons_record_alone_or_with_modifiers() => OnSta(box =>
    {
        box.HandleKeyDown(Keys.XButton1, Keys.None, false);
        Assert.Equal("XButton1", box.Value.ToString());
        Assert.Equal("Mouse button 4 (back)", box.Value.DisplayText);
        box.HandleKeyDown(Keys.MButton, Keys.Control, false);
        Assert.Equal("Ctrl+MButton", box.Value.ToString());
        Assert.True(Hotkey.TryParse("Ctrl+XButton2", out var hk) && hk.IsMouse);
        Assert.False(Hotkey.TryParse("LButton", out _)); // left and right click can't be shortcuts
        Assert.False(Hotkey.TryParse("RButton", out _));
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
