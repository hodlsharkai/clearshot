using System.Runtime.InteropServices;
using ClearShot.Capture;

namespace ClearShot.Tests;

/// <summary>
/// Captures the running ClearShot window exactly as Windows draws it (PrintWindow, so other windows on top
/// don't matter). Use this, not an off-screen DrawToBitmap, to judge real alignment and theming.
/// Only runs with CLEARSHOT_GRAB set to an output path.
/// </summary>
public class RealWindowGrabTests
{
    [Fact]
    public void Grab_running_window()
    {
        var output = Environment.GetEnvironmentVariable("CLEARSHOT_GRAB");
        if (string.IsNullOrEmpty(output)) return;
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        var proc = System.Diagnostics.Process.GetProcessesByName("ClearShot").Single();
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out uint pid);
            var sb = new System.Text.StringBuilder(100);
            GetWindowText(h, sb, 100);
            if (pid == proc.Id && IsWindowVisible(h) && sb.ToString() == "ClearShot") { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, found);
        GetWindowRect(found, out var r);
        var rect = Rectangle.FromLTRB(r.L, r.T, r.R, r.B);
        using var bmp = new Bitmap(rect.Width, rect.Height);
        using (var g = Graphics.FromImage(bmp))
        {
            var hdc = g.GetHdc();
            PrintWindow(found, hdc, 2 /* PW_RENDERFULLCONTENT */);
            g.ReleaseHdc(hdc);
        }
        bmp.Save(output);
        Console.WriteLine($"GRAB: window {rect} dpi {GetDpiForWindow(found)}");
    }

    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out Rct r);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);
    struct Rct { public int L, T, R, B; }
}
