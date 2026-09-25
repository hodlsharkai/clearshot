using ClearShot.Capture;
using SixLabors.ImageSharp;
using Image = SixLabors.ImageSharp.Image;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors.Quantization;

namespace ClearShot;

/// <summary>Turns a recording into a looping GIF. Uses ImageSharp (Apache 2.0 for open-source projects).</summary>
internal static class GifMaker
{
    /// <remarks>Each frame's pixels are released once added, so the recording isn't held in memory twice.</remarks>
    public static void Save(Recording recording, string path)
    {
        var delays = CentisecondDelays(recording.Frames.Select(f => f.DurationMs).ToArray());
        using var gif = Image.LoadPixelData<Bgra32>(recording.Frames[0].Bgra, recording.Width, recording.Height);
        recording.Frames[0].Release();
        gif.Metadata.GetGifMetadata().RepeatCount = 0; // loop forever
        gif.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = delays[0];
        for (int i = 1; i < recording.Frames.Count; i++)
        {
            using (var next = Image.LoadPixelData<Bgra32>(recording.Frames[i].Bgra, recording.Width, recording.Height))
            {
                var frame = gif.Frames.AddFrame(next.Frames.RootFrame);
                frame.Metadata.GetGifMetadata().FrameDelay = delays[i];
            }
            recording.Frames[i].Release();
        }

        var encoder = new GifEncoder
        {
            // A palette per frame keeps colours right when the content changes. No dithering: measured on a dark
            // game-like scene (25/09), ordered dithering was ~8x less accurate, speckled blacks with grey dots,
            // shimmered and doubled the file size; plain Wu quantisation was best on every count.
            ColorTableMode = GifColorTableMode.Local,
            Quantizer = new WuQuantizer(new QuantizerOptions { Dither = null }),
        };
        gif.SaveAsGif(path, encoder);
    }

    /// <summary>
    /// GIF delays are in hundredths of a second. Rounding each frame separately drifts, so round the running
    /// total instead; also respect the 2-hundredths minimum that browsers enforce.
    /// </summary>
    internal static int[] CentisecondDelays(int[] durationsMs)
    {
        var delays = new int[durationsMs.Length];
        long elapsedMs = 0;
        int elapsedCs = 0;
        for (int i = 0; i < durationsMs.Length; i++)
        {
            elapsedMs += durationsMs[i];
            int targetCs = (int)Math.Round(elapsedMs / 10.0);
            delays[i] = Math.Max(2, targetCs - elapsedCs);
            elapsedCs += delays[i];
        }
        return delays;
    }
}
