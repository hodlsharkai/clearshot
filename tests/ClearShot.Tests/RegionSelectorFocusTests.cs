using System.Runtime.InteropServices;

namespace ClearShot.Tests;

public class RegionSelectorFocusTests
{
    /// <summary>
    /// The bug from 24/09: the selection overlay took focus, and apps that pause video on losing focus
    /// (WhatsApp's player, many games) paused. The overlay must leave the foreground window alone.
    /// </summary>
    [Fact]
    public void Showing_the_overlay_does_not_steal_focus()
    {
        Exception? failure = null;
        var t = new Thread(() =>
        {
            try
            {
                var before = GetForegroundWindow();
                using var frozen = new Bitmap(200, 150);
                using var selector = new RegionSelector(frozen, new Rectangle(-30000, -30000, 200, 150));
                var selecting = selector.SelectAsync();
                for (int i = 0; i < 10; i++) { Application.DoEvents(); Thread.Sleep(20); }

                Assert.True(selector.Visible);
                Assert.NotEqual(selector.Handle, GetForegroundWindow());
                Assert.Equal(before, GetForegroundWindow());
                Assert.NotEqual(0, GetWindowLong(selector.Handle, -20) & 0x08000000); // WS_EX_NOACTIVATE

                selector.Cancel();
                Application.DoEvents();
                Assert.True(selecting.IsCompleted);
                Assert.Null(selecting.Result);
            }
            catch (Exception ex) { failure = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (failure is not null) throw failure;
    }

    /// <summary>The live picker (the default) must also leave focus alone, and Esc/cancel must end it cleanly.</summary>
    [Fact]
    public void Live_picker_does_not_steal_focus_and_cancels_cleanly()
    {
        Exception? failure = null;
        var t = new Thread(() =>
        {
            try
            {
                var before = GetForegroundWindow();
                using var live = new LiveRegionSelector(new Rectangle(-30000, -30000, 300, 200));
                var picking = live.SelectAsync();
                for (int i = 0; i < 10; i++) { Application.DoEvents(); Thread.Sleep(20); }
                Assert.Equal(before, GetForegroundWindow());
                live.Cancel();
                Application.DoEvents();
                Assert.True(picking.IsCompleted);
                Assert.Null(picking.Result);
            }
            catch (Exception ex) { failure = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (failure is not null) throw failure;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
}
