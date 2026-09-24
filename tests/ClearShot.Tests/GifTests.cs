using System.Runtime.InteropServices;
using ClearShot.Capture;
using SixLabors.ImageSharp;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;
using SixLabors.ImageSharp.Formats.Gif;
using ISImage = SixLabors.ImageSharp.Image;

namespace ClearShot.Tests;

[Collection("Screen")] // screen-capturing tests take turns, as captures do in the app
public class GifTests
{
    private static Recording Synthetic(int frames, int w = 40, int h = 30)
    {
        var list = new List<RecordedFrame>();
        for (int i = 0; i < frames; i++)
        {
            var px = new byte[w * h * 4];
            for (int p = 0; p < w * h; p++)
            {
                px[p * 4] = (byte)(i * 60);      // B
                px[p * 4 + 1] = (byte)(p % 256); // G
                px[p * 4 + 2] = 200;             // R
                px[p * 4 + 3] = 255;
            }
            list.Add(new RecordedFrame(px, 67));
        }
        return new Recording(list, w, h);
    }

    [Fact]
    public void Writes_a_looping_animated_gif_with_every_frame()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gif");
        try
        {
            GifMaker.Save(Synthetic(4), path);
            using var gif = ISImage.Load(path);
            Assert.Equal(4, gif.Frames.Count);
            Assert.Equal(40, gif.Width);
            Assert.Equal(0, gif.Metadata.GetGifMetadata().RepeatCount);
            var total = Enumerable.Range(0, 4).Sum(i => gif.Frames[i].Metadata.GetGifMetadata().FrameDelay);
            Assert.InRange(total, 26, 28); // 4 x 67 ms = 268 ms
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Delays_follow_the_running_total_and_respect_the_minimum()
    {
        var delays = GifMaker.CentisecondDelays(Enumerable.Repeat(67, 15).Append(5).ToArray());
        Assert.InRange(delays.Take(15).Sum(), 100, 101); // 15 frames at 15 fps is about 1 second
        Assert.Equal(2, delays[^1]);                    // never below 2 hundredths
    }

    [Fact]
    public void Gif_is_copied_as_a_file_so_discord_uploads_the_animation()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gif");
        File.WriteAllText(path, "x");
        Exception? failure = null;
        var t = new Thread(() =>
        {
            try
            {
                ClipboardOutput.CopyFile(path);
                var files = Clipboard.GetFileDropList();
                Assert.Single(files);
                Assert.Equal(path, files[0]);
            }
            catch (Exception ex) { failure = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        File.Delete(path);
        if (failure is not null) throw failure;
    }

    /// <summary>Records real footage of the primary screen. Only with CLEARSHOT_LIVE=1.</summary>
    [Fact]
    public async Task Records_the_real_screen_at_full_speed()
    {
        if (Environment.GetEnvironmentVariable("CLEARSHOT_LIVE") != "1") return;
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        var primary = Screen.PrimaryScreen!.Bounds;
        var monitor = ScreenCapturer.MonitorFromPoint(new Point(primary.X + 10, primary.Y + 10), 2);

        foreach (var area in new[] { new Rectangle(100, 100, 800, 450), new Rectangle(Point.Empty, primary.Size) })
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var recording = await new RegionRecorder(monitor, area, 15, 960, TimeSpan.FromSeconds(15)).RunAsync(stop.Token);
            var recordMs = clock.ElapsedMilliseconds;
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gif");
            clock.Restart();
            GifMaker.Save(recording, path);
            var encodeMs = clock.ElapsedMilliseconds;
            var mb = new FileInfo(path).Length / 1048576.0;
            File.Delete(path);
            Console.WriteLine($"LIVE: area {area.Size} -> {recording.Width}x{recording.Height}, {recording.Frames.Count} distinct frames, " +
                              $"length {recording.TotalMs} ms (recorded for {recordMs} ms), encode {encodeMs} ms, {mb:0.00} MB");
            Assert.True(recording.Width <= 960);
            Assert.InRange(recording.TotalMs, 1700, 2300);
        }
    }

    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr v);
}
