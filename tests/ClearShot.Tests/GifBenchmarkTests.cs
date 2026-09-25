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
        // Each variant: darkDither:darkRampEnd[:busyScale[:refinePasses]]; default = the shipped settings.
        var variants = (Environment.GetEnvironmentVariable("CLEARSHOT_CMP_DARK") ?? "0.3:48:3:4").Split(',');
        foreach (var v in variants)
        {
            var parts = v.Split(':');
            GifWriter.DarkDither = float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
            GifWriter.DarkRampEnd = int.Parse(parts[1]);
            GifWriter.BusyScale = parts.Length > 2 ? float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) : 0;
            GifWriter.RefinePasses = parts.Length > 3 ? int.Parse(parts[3]) : 0;
            var copy = frames.Select(f => new RecordedFrame((byte[])f.Bgra.Clone(), f.DurationMs)).ToList();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var path = Path.Combine(dir, $"cs_{string.Join("_", parts)}.gif");
            GifWriter.Save(new Recording(copy, w, h), path);
            Console.WriteLine($"BENCH: {v} -> {new FileInfo(path).Length / 1024} KB in {sw.ElapsedMilliseconds} ms");
        }
    }
}
