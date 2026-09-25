using ClearShot.Capture;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ISImage = SixLabors.ImageSharp.Image;

namespace ClearShot.Tests;

/// <summary>
/// Encodes a folder of reference frames (ref/*.png, e.g. pulled from a ClearShot MP4) with ClearShot's GIF
/// encoder, for comparison against other encoders. Only runs with CLEARSHOT_CMP set to that folder.
/// </summary>
public class GifBenchmarkTests
{
    [Fact]
    public void Encode_reference_frames()
    {
        var dir = Environment.GetEnvironmentVariable("CLEARSHOT_CMP");
        if (string.IsNullOrEmpty(dir)) return;
        int w = 0, h = 0;
        var frames = Directory.GetFiles(Path.Combine(dir, "ref"), "*.png").OrderBy(f => f).Select(f =>
        {
            using var im = ISImage.Load<Bgra32>(f);
            (w, h) = (im.Width, im.Height);
            var bytes = new byte[w * h * 4];
            im.CopyPixelDataTo(bytes);
            return new RecordedFrame(bytes, 33);
        }).ToList();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var path = Path.Combine(dir, "clearshot_final.gif");
        GifWriter.Save(new Recording(frames, w, h), path);
        Console.WriteLine($"BENCH: {frames.Count} frames {w}x{h} -> {new FileInfo(path).Length / 1024} KB in {sw.ElapsedMilliseconds} ms");
    }
}
