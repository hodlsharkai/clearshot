using ClearShot.Capture;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ISImage = SixLabors.ImageSharp.Image;

namespace ClearShot.Tests;

public class GifWriterTests
{
    /// <summary>
    /// LZW must be exact. Noisy indices fill the 4096-entry table many times over, exercising code-size growth
    /// and the clear/reset path. The decoded indices must match exactly. (Also checked with Pillow; see
    /// CLEARSHOT_GIF_DUMP.)
    /// </summary>
    [Fact]
    public void Lzw_round_trips_exactly_through_imagesharp()
    {
        int w = 500, h = 400;
        var rng = new Random(42);
        var indices = new byte[w * h];
        for (int i = 0; i < indices.Length; i++)
            indices[i] = (byte)(i % 7 == 0 ? rng.Next(256) : (i / 13 + rng.Next(3)) % 256); // mix of runs and noise

        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gif");
        using (var file = File.Create(path))
        {
            file.Write("GIF89a"u8);
            file.Write([(byte)w, (byte)(w >> 8), (byte)h, (byte)(h >> 8), 0x70, 0, 0]);
            file.Write([0x2C, 0, 0, 0, 0, (byte)w, (byte)(w >> 8), (byte)h, (byte)(h >> 8), 0x87]);
            for (int c = 0; c < 256; c++) file.Write([(byte)c, (byte)(255 - c), (byte)(c * 7)]); // distinct colours
            GifWriter.Lzw.Encode(indices, file);
            file.WriteByte(0x3B);
        }
        var dump = Environment.GetEnvironmentVariable("CLEARSHOT_GIF_DUMP");
        if (!string.IsNullOrEmpty(dump)) { File.Copy(path, Path.Combine(dump, "lzw.gif"), true); File.WriteAllBytes(Path.Combine(dump, "lzw.idx"), indices); }
        try
        {
            using var img = ISImage.Load<Rgba32>(path);
            for (int i = 0; i < indices.Length; i++)
            {
                var p = img[i % w, i / w];
                byte c = indices[i];
                Assert.True(p.R == c && p.G == (byte)(255 - c) && p.B == (byte)(c * 7), $"pixel {i}: expected index {c}");
            }
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// The complaint from 25/09: dark shading broke into blotches. A smooth near-black gradient must come out
    /// smooth when viewed normally (slightly blurred, as the eye sees it), and true black must stay black.
    /// </summary>
    [Fact]
    public void Dark_gradients_stay_smooth_and_black_stays_black()
    {
        int w = 256, h = 64;
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int o = (y * w + x) * 4;
            byte v = (byte)(x < 32 ? 0 : (x - 32) * 36 / (w - 32)); // pure black, then a gentle ramp up to 36
            px[o] = (byte)(v * 0.9); px[o + 1] = v; px[o + 2] = (byte)(v * 0.8); px[o + 3] = 255;
        }
        var source = (byte[])px.Clone();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gif");
        try
        {
            GifWriter.Save(new Recording([new RecordedFrame(px, 100)], w, h), path);
            using var gif = ISImage.Load<Rgba32>(path);
            double worst = 0;
            for (int x = 34; x < w - 3; x++)
            {
                double got = 0, want = 0;
                for (int y = 8; y < h - 8; y++)
                for (int dx = -2; dx <= 2; dx++) { got += gif[x + dx, y].G; want += source[(y * w + x + dx) * 4 + 1]; }
                worst = Math.Max(worst, Math.Abs(got - want) / ((h - 16) * 5));
            }
            Assert.True(worst < 1.5, $"blurred gradient off by up to {worst:0.00} levels (blotchy)");
            for (int x = 0; x < 28; x++)
            for (int y = 0; y < h; y++)
                Assert.True(gif[x, y].R + gif[x, y].G + gif[x, y].B <= 3, $"black pixel at {x},{y} is {gif[x, y]}");
        }
        finally { File.Delete(path); }
    }

    /// <summary>A still gradient with a moving box: the gradient must stay pixel-identical frame to frame.</summary>
    [Fact]
    public void Still_areas_never_flicker_and_moving_areas_update()
    {
        int w = 200, h = 120, n = 6;
        var frames = new List<RecordedFrame>();
        for (int t = 0; t < n; t++)
        {
            var px = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                byte v = (byte)(x * 40 / w); // dark gradient 0..40
                bool box = x >= 20 + t * 20 && x < 50 + t * 20 && y >= 40 && y < 80;
                px[o] = box ? (byte)30 : v; px[o + 1] = box ? (byte)120 : v; px[o + 2] = box ? (byte)220 : (byte)(v / 2); px[o + 3] = 255;
            }
            frames.Add(new RecordedFrame(px, 100));
        }
        var source = frames.Select(f => (byte[])f.Bgra.Clone()).ToList();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gif");
        try
        {
            GifWriter.Save(new Recording(frames, w, h), path);
            using var gif = ISImage.Load<Rgba32>(path);
            Assert.Equal(n, gif.Frames.Count);
            for (int t = 1; t < n; t++)
            {
                for (int y = 0; y < h; y += 2)
                for (int x = 0; x < w; x += 2)
                {
                    bool touched = y >= 40 && y < 80 && x >= 20 + (t - 1) * 20 && x < 50 + t * 20;
                    if (!touched) Assert.Equal(gif.Frames[t - 1][x, y], gif.Frames[t][x, y]); // still area: identical
                }
                // The box moved: its new position shows its colour (within dithering error).
                var inBox = gif.Frames[t][35 + t * 20, 60];
                Assert.InRange(inBox.R, 200, 240);
                Assert.InRange(inBox.B, 15, 45);
            }
            // Overall accuracy on the final displayed frame.
            double err = 0;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = gif.Frames[n - 1][x, y]; int o = (y * w + x) * 4;
                err += Math.Abs(p.R - source[n - 1][o + 2]) + Math.Abs(p.G - source[n - 1][o + 1]) + Math.Abs(p.B - source[n - 1][o]);
            }
            Assert.True(err / (w * h * 3) < 3, $"mean error {err / (w * h * 3):0.00}");
        }
        finally { File.Delete(path); }
    }
}
