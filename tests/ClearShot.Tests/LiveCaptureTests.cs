using System.Runtime.InteropServices;
using ClearShot.Capture;

namespace ClearShot.Tests;

/// <summary>
/// Captures the real screen, so it only runs when CLEARSHOT_LIVE=1 is set. Nothing is saved.
/// </summary>
[Collection("Screen")] // screen-capturing tests take turns, as captures do in the app
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
    /// Only pixels drawn at Windows' normal SDR white are compared: some apps (Chromium-based ones such as
    /// Brave and Discord) show up in the HDR data at 80-nit white, and GDI brightens those back up.
    /// </summary>
    [Fact]
    public void Hdr_tone_mapping_matches_windows_sdr_rendering()
    {
        if (Environment.GetEnvironmentVariable("CLEARSHOT_LIVE") != "1") return;
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        var primary = Screen.PrimaryScreen!.Bounds;
        var point = new Point(primary.X + 10, primary.Y + 10);

        // Windows' capture is taken just before and just after ours; only pixels identical in both are compared,
        // so anything moving on screen (a video, a game) can't skew the result.
        using var windows = ScreenCapturer.CaptureWithGdi(ScreenCapturer.MonitorFromPoint(point, 2));
        using var ours = ScreenCapturer.CaptureMonitorAt(point, keepHdr: true);
        using var windowsAfter = ScreenCapturer.CaptureWithGdi(ScreenCapturer.MonitorFromPoint(point, 2));
        if (ours.Hdr is not { } hdr) { Console.WriteLine("LIVE: desktop is SDR, HDR comparison skipped"); return; }
        float sdrWhiteNits = DisplayInfo.SdrWhiteScRgb(Screen.PrimaryScreen.DeviceName) * 80f;

        double diff = 0;
        int n = 0;
        for (int y = 0; y < hdr.Height; y += 7)
        for (int x = 0; x < hdr.Width; x += 7)
        {
            var b = windows.Image.GetPixel(x, y);
            if (b != windowsAfter.Image.GetPixel(x, y)) continue;
            if (b.G < 60 || b.G > 240) continue;
            double linear = Math.Pow((b.G / 255.0 + 0.055) / 1.055, 2.4);
            double white = (float)hdr.Pixels[(y * hdr.Width + x) * 4 + 1] * 80.0 / linear;
            if (Math.Abs(white / sdrWhiteNits - 1) > 0.15) continue;
            var a = ours.Image.GetPixel(x, y);
            diff += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
            n += 3;
        }
        if (n == 0) { Console.WriteLine("LIVE: no comparable pixels on screen"); return; }
        Console.WriteLine($"LIVE: HDR vs Windows SDR on {n / 3} normal-white pixels: mean abs diff {diff / n:0.00} levels");
        Assert.True(diff / n < 3, $"tone-mapped output differs from Windows by {diff / n:0.00} levels on average");
    }

    [Fact]
    public void Saves_true_hdr_copies_of_the_real_screen()
    {
        if (Environment.GetEnvironmentVariable("CLEARSHOT_LIVE") != "1") return;
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        var primary = Screen.PrimaryScreen!.Bounds;
        using var shot = ScreenCapturer.CaptureMonitorAt(new Point(primary.X + 10, primary.Y + 10), keepHdr: true);
        if (!shot.WasHdr) { Console.WriteLine("LIVE: desktop is SDR, HDR copies skipped"); return; }
        Assert.NotNull(shot.Hdr);

        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            HdrWriters.WriteJxr(shot.Hdr!, Path.Combine(dir, "a.jxr"));
            var jxrMs = sw.ElapsedMilliseconds;
            sw.Restart();
            HdrWriters.WritePqPng(shot.Hdr!, Path.Combine(dir, "a.png"));
            var pngMs = sw.ElapsedMilliseconds;
            var jxrMb = new FileInfo(Path.Combine(dir, "a.jxr")).Length / 1e6;
            var pngMb = new FileInfo(Path.Combine(dir, "a.png")).Length / 1e6;
            Console.WriteLine($"LIVE: jxr {jxrMb:0.0} MB in {jxrMs} ms, HDR png {pngMb:0.0} MB in {pngMs} ms");
            Assert.True(jxrMb > 0.1 && pngMb > 0.1);
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>
    /// Saves both HDR formats from the real screen, reads them back, and checks the brightness in the files
    /// matches what was captured (in nits), so the files hold real HDR data rather than just being valid files.
    /// </summary>
    [Fact]
    public void Hdr_files_read_back_with_the_captured_brightness()
    {
        if (Environment.GetEnvironmentVariable("CLEARSHOT_LIVE") != "1") return;
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        var primary = Screen.PrimaryScreen!.Bounds;
        using var shot = ScreenCapturer.CaptureMonitorAt(new Point(primary.X + 10, primary.Y + 10), keepHdr: true);
        if (shot.Hdr is not { } hdr) { Console.WriteLine("LIVE: desktop is SDR, skipped"); return; }

        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            // JPEG XR: decode with Windows' own codec and compare scRGB values (as nits).
            var jxrPath = Path.Combine(dir, "a.jxr");
            HdrWriters.WriteJxr(hdr, jxrPath);
            var jxr = DecodeJxr(jxrPath, hdr.Width, hdr.Height);
            var (jxrErr, jxrPeak) = CompareNits(hdr, i => (float)jxr[i] * 80f);
            Console.WriteLine($"LIVE: jxr mean error {jxrErr:0.00} nits (brightest pixel captured: {jxrPeak:0} nits)");
            Assert.True(jxrErr < 2.0, $"jxr brightness off by {jxrErr:0.00} nits on average");

            // HDR PNG: undo the PQ curve and the Rec.2020 conversion, compare nits.
            var pngPath = Path.Combine(dir, "a.png");
            HdrWriters.WritePqPng(hdr, pngPath);
            var pngNits = DecodePqPngToRec709Nits(pngPath, hdr.Width, hdr.Height);
            var (pngErr, _) = CompareNits(hdr, i => pngNits[i]);
            Console.WriteLine($"LIVE: HDR png mean error {pngErr:0.00} nits");
            Assert.True(pngErr < 1.0, $"HDR png brightness off by {pngErr:0.00} nits on average");
        }
        finally { Directory.Delete(dir, true); }
    }

    private static (double MeanError, double Peak) CompareNits(HdrFrame hdr, Func<int, float> decodedNits)
    {
        double err = 0, peak = 0;
        long n = 0;
        for (int y = 0; y < hdr.Height; y += 5)
        for (int x = 0; x < hdr.Width; x += 5)
        for (int c = 0; c < 3; c++)
        {
            int i = (y * hdr.Width + x) * 4 + c;
            double original = Math.Max(0, (float)hdr.Pixels[i]) * 80.0;
            peak = Math.Max(peak, original);
            err += Math.Abs(decodedNits(i) - original);
            n++;
        }
        return (err / n, peak);
    }

    private static Half[] DecodeJxr(string path, int width, int height)
    {
        using var factory = new Vortice.WIC.IWICImagingFactory();
        using var decoder = factory.CreateDecoderFromFileName(path);
        using var frame = decoder.GetFrame(0);
        using var converter = factory.CreateFormatConverter();
        converter.Initialize(frame, Vortice.WIC.PixelFormat.Format64bppRGBAHalf, Vortice.WIC.BitmapDitherType.None, null, 0, Vortice.WIC.BitmapPaletteType.Custom);
        var bytes = new byte[width * height * 8];
        converter.CopyPixels((uint)(width * 8), bytes);
        return System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>(bytes).ToArray();
    }

    private static float[] DecodePqPngToRec709Nits(string path, int width, int height)
    {
        var png = File.ReadAllBytes(path);
        using var idat = new MemoryStream();
        for (int pos = 8; pos < png.Length;)
        {
            int len = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
            if (System.Text.Encoding.ASCII.GetString(png, pos + 4, 4) == "IDAT") idat.Write(png, pos + 8, len);
            pos += 12 + len;
        }
        idat.Position = 0;
        using var zlib = new System.IO.Compression.ZLibStream(idat, System.IO.Compression.CompressionMode.Decompress);
        int rowBytes = width * 6;
        var prev = new byte[rowBytes];
        var row = new byte[rowBytes + 1];
        var nits = new float[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            zlib.ReadExactly(row);
            for (int i = 0; i < rowBytes; i++) prev[i] = (byte)(row[i + 1] + (row[0] == 2 ? prev[i] : 0));
            for (int x = 0; x < width; x++)
            {
                double r = PqToNits(prev, x * 6), g = PqToNits(prev, x * 6 + 2), b = PqToNits(prev, x * 6 + 4);
                // Rec.2020 back to Rec.709.
                int o = (y * width + x) * 4;
                nits[o] = (float)(1.6604910 * r - 0.5876411 * g - 0.0728499 * b);
                nits[o + 1] = (float)(-0.1245505 * r + 1.1328999 * g - 0.0083494 * b);
                nits[o + 2] = (float)(-0.0181508 * r - 0.1005789 * g + 1.1187297 * b);
            }
        }
        return nits;
    }

    private static double PqToNits(byte[] row, int offset)
    {
        const double m1 = 2610.0 / 16384, m2 = 2523.0 / 4096 * 128;
        const double c1 = 3424.0 / 4096, c2 = 2413.0 / 4096 * 32, c3 = 2392.0 / 4096 * 32;
        double e = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(row.AsSpan(offset)) / 65535.0;
        double p = Math.Pow(e, 1 / m2);
        return 10000 * Math.Pow(Math.Max(p - c1, 0) / (c2 - c3 * p), 1 / m1);
    }

    /// <summary>
    /// GIF recording shrinks 4K HDR by averaging 2x2 blocks in linear light before tone mapping (for speed).
    /// It must look the same as tone mapping at full size and then shrinking.
    /// </summary>
    [Fact]
    public unsafe void Binned_hdr_matches_full_size_tone_mapping()
    {
        if (Environment.GetEnvironmentVariable("CLEARSHOT_LIVE") != "1") return;
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        var primary = Screen.PrimaryScreen!.Bounds;
        using var shot = ScreenCapturer.CaptureMonitorAt(new Point(primary.X + 10, primary.Y + 10), keepHdr: true);
        if (shot.Hdr is not { } hdr) { Console.WriteLine("LIVE: desktop is SDR, binning check skipped"); return; }
        var mapper = new HdrToneMapper(DisplayInfo.SdrWhiteScRgb(Screen.PrimaryScreen.DeviceName), 1.5f);
        int w = hdr.Width, h = hdr.Height;
        fixed (Half* p = hdr.Pixels)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var binned = RegionRecorder.ToneMapBinned((IntPtr)p, w * 8, w, h, 2, mapper);
            var binnedMs = sw.ElapsedMilliseconds;
            sw.Restart();
            using var full = ScreenCapturer.ToneMap((IntPtr)p, w * 8, w, h, mapper);
            var fullMs = sw.ElapsedMilliseconds;
            using var shrunk = new Bitmap(w / 2, h / 2);
            using (var g = Graphics.FromImage(shrunk))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(full, new Rectangle(0, 0, w / 2, h / 2));
            }
            double diff = 0; long n = 0;
            for (int y = 0; y < h / 2; y += 5)
            for (int x = 0; x < w / 2; x += 5)
            {
                var a = binned.GetPixel(x, y); var b = shrunk.GetPixel(x, y);
                diff += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B); n += 3;
            }
            Console.WriteLine($"LIVE: binned HDR {binnedMs} ms vs full {fullMs} ms per 4K frame; mean difference {diff / n:0.00} levels");
            Assert.True(diff / n < 3, $"binned output differs by {diff / n:0.00} levels");
        }
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
