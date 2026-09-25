using System.Runtime.InteropServices.WindowsRuntime;
using ClearShot.Capture;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace ClearShot;

/// <summary>
/// Saves a recording as an H.264 MP4 with Windows' own encoder (hardware-accelerated where available):
/// full colour, no banding, and far smaller than a GIF. Frames keep their real timings.
/// </summary>
internal static class Mp4Maker
{
    public static async Task SaveAsync(Recording recording, string path)
    {
        // H.264 needs even dimensions; drop a single edge pixel if needed.
        int width = recording.Width & ~1, height = recording.Height & ~1;
        if (width < 2 || height < 2) throw new ArgumentException("The recording is too small for a video.");

        var input = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)width, (uint)height);
        var source = new MediaStreamSource(new VideoStreamDescriptor(input))
        {
            BufferTime = TimeSpan.Zero,
            Duration = TimeSpan.FromMilliseconds(recording.TotalMs),
        };
        int next = 0;
        var timestamp = TimeSpan.Zero;
        source.Starting += (_, e) => e.Request.SetActualStartPosition(TimeSpan.Zero);
        source.SampleRequested += (_, e) =>
        {
            if (next >= recording.Frames.Count) return; // no sample: end of stream
            var frame = recording.Frames[next++];
            var pixels = FlipAndCrop(frame.Bgra, recording.Width, width, height);
            var sample = MediaStreamSample.CreateFromBuffer(pixels.AsBuffer(), timestamp);
            sample.Duration = TimeSpan.FromMilliseconds(frame.DurationMs);
            timestamp += sample.Duration;
            e.Request.Sample = sample;
        };

        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        profile.Audio = null;
        profile.Video.Width = (uint)width;
        profile.Video.Height = (uint)height;
        profile.Video.FrameRate.Numerator = 30;
        profile.Video.FrameRate.Denominator = 1;
        // Generous for screen content: sharp text, still small next to the GIF.
        profile.Video.Bitrate = (uint)Math.Clamp(width * height * 6L, 1_000_000, 16_000_000);

        File.Create(path).Dispose();
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
        var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, stream, profile);
        if (!prepared.CanTranscode) throw new InvalidOperationException($"Windows couldn't make the MP4 ({prepared.FailureReason}).");
        await prepared.TranscodeAsync();
    }

    /// <summary>
    /// Windows' encoder reads uncompressed RGB bottom row first, so rows go in reversed (otherwise the video is
    /// upside down); this also trims to the even size H.264 needs.
    /// </summary>
    private static byte[] FlipAndCrop(byte[] bgra, int stride, int width, int height)
    {
        var flipped = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            Buffer.BlockCopy(bgra, y * stride * 4, flipped, (height - 1 - y) * width * 4, width * 4);
        return flipped;
    }
}
