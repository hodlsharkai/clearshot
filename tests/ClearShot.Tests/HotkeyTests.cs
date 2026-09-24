using System.Windows.Forms;

namespace ClearShot.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("PrintScreen", Keys.PrintScreen, false, false, false, false)]
    [InlineData("Ctrl+PrintScreen", Keys.PrintScreen, true, false, false, false)]
    [InlineData("Ctrl+Shift+S", Keys.S, true, false, true, false)]
    [InlineData("Win+Alt+F12", Keys.F12, false, true, false, true)]
    public void Parses(string text, Keys key, bool ctrl, bool alt, bool shift, bool win)
    {
        Assert.True(Hotkey.TryParse(text, out var hk));
        Assert.Equal(key, hk.Key);
        Assert.Equal(ctrl, hk.Ctrl);
        Assert.Equal(alt, hk.Alt);
        Assert.Equal(shift, hk.Shift);
        Assert.Equal(win, hk.Win);
    }

    [Theory]
    [InlineData("Ctrl+PrintScreen")]
    [InlineData("Ctrl+Alt+Shift+Win+F9")]
    [InlineData("PrintScreen")]
    [InlineData("Alt+Shift+C")]
    public void Round_trips(string text)
    {
        Assert.True(Hotkey.TryParse(text, out var hk));
        Assert.Equal(text, hk.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Banana")]
    public void Rejects_invalid(string text)
    {
        Assert.False(Hotkey.TryParse(text, out _));
    }

    [Fact]
    public void Parsing_is_case_insensitive_and_accepts_control()
    {
        Assert.True(Hotkey.TryParse("control+shift+s", out var hk));
        Assert.Equal("Ctrl+Shift+S", hk.ToString());
    }

    [Fact]
    public void Display_text_is_friendly()
    {
        Assert.True(Hotkey.TryParse("Ctrl+PrintScreen", out var hk));
        Assert.Equal("Ctrl + Print Screen", hk.DisplayText);
    }
}
