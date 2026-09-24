using System.Runtime.InteropServices;
using ClearShot.Capture;

namespace ClearShot.Tests;

/// <summary>
/// Captures the real screen, so it only runs when CLEARSHOT_LIVE=1 is set. Nothing is saved.
/// </summary>
public class LiveCaptureTests
{
    [Fact]
    public void Captures_primary_monitor_at_native_resolution()
    {
        if (Environment.GetEnvironmentVariable("CLEARSHOT_LIVE") != "1") return;

        // The app is per-monitor DPI aware (Desktop Duplication requires it); make the test host match.
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        var primary = Screen.PrimaryScreen!.Bounds;
        var point = new Point(primary.X + 10, primary.Y + 10);
        var started = System.Diagnostics.Stopwatch.StartNew();
        ScreenCapturer.CaptureMonitorAt(point).Dispose();
        var cold = started.ElapsedMilliseconds;
        started.Restart();
        using var shot = ScreenCapturer.CaptureMonitorAt(point);
        var warm = started.ElapsedMilliseconds;

        Console.WriteLine($"LIVE: {shot.Image.Width}x{shot.Image.Height} bounds={shot.Bounds} hdr={shot.WasHdr} fallback={shot.UsedFallback} cold={cold} ms warm={warm} ms");
        Assert.Equal(primary.Size, shot.Image.Size);
        Assert.Equal(primary, shot.Bounds);
        Assert.False(shot.UsedFallback, "Desktop Duplication should work on a normal desktop");
    }

    /// <summary>
    /// On an HDR desktop, compares our tone-mapped capture with Windows' own SDR conversion (GDI).
    /// Desktop content is SDR, so the two should nearly match; a big gap means washed-out or crushed output.
    /// </summary>
    [Fact]
    public void Hdr_tone_mapping_matches_windows_sdr_rendering()
    {
        if (Environment.GetEnvironmentVariable("CLEARSHOT_LIVE") != "1") return;
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        var primary = Screen.PrimaryScreen!.Bounds;
        var point = new Point(primary.X + 10, primary.Y + 10);

        using var ours = ScreenCapturer.CaptureMonitorAt(point);
        if (!ours.WasHdr) { Console.WriteLine("LIVE: desktop is SDR, HDR comparison skipped"); return; }
        using var windows = ScreenCapturer.CaptureWithGdi(ScreenCapturer.MonitorFromPoint(point, 2));

        double diff = 0, oursLum = 0, winLum = 0;
        int n = 0;
        for (int y = 0; y < ours.Image.Height; y += 7)
        for (int x = 0; x < ours.Image.Width; x += 7)
        {
            var a = ours.Image.GetPixel(x, y);
            var b = windows.Image.GetPixel(x, y);
            diff += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
            oursLum += a.R + a.G + a.B;
            winLum += b.R + b.G + b.B;
            n += 3;
        }
        Console.WriteLine($"LIVE: HDR vs Windows SDR: mean abs diff {diff / n:0.00} levels, mean level ours {oursLum / n:0.0} vs Windows {winLum / n:0.0}");
        Assert.True(diff / n < 6, $"tone-mapped output differs from Windows by {diff / n:0.00} levels on average");
    }

    /// <summary>Read-only: connects to Windows' media controls and lists what's playing. Pauses nothing.</summary>
    [Fact]
    public async Task Can_read_windows_media_sessions()
    {
        if (Environment.GetEnvironmentVariable("CLEARSHOT_LIVE") != "1") return;
        var playing = await MediaPauser.PlayingAppsAsync();
        Console.WriteLine($"LIVE: media playing now: {(playing.Count == 0 ? "nothing" : string.Join(", ", playing))}");
    }

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
}
