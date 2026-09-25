using ClearShot.Capture;
using Windows.Media.Editing;
using Windows.Storage;

namespace ClearShot.Tests;

public class Mp4Tests
{
    /// <summary>Top half red, bottom half blue, so an upside-down video is caught.</summary>
    private static Recording RedOverBlue(int w, int h, int frames, int ms)
    {
        var list = new List<RecordedFrame>();
        for (int i = 0; i < frames; i++)
        {
            var px = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                bool top = y < h / 2;
                px[o] = (byte)(top ? 0 : 255); px[o + 1] = 0; px[o + 2] = (byte)(top ? 255 : 0); px[o + 3] = 255;
            }
            list.Add(new RecordedFrame(px, ms));
        }
        return new Recording(list, w, h);
    }

    [Theory]
    [InlineData(320, 240)]
    [InlineData(321, 241)] // odd sizes: H.264 needs even, so one edge pixel is dropped
    public async Task Mp4_is_upright_and_the_right_length(int w, int h)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4");
        try
        {
            await Mp4Maker.SaveAsync(RedOverBlue(w, h, 10, 100), path);
            Assert.True(new FileInfo(path).Length > 1000);

            var clip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(path));
            Assert.InRange(clip.OriginalDuration.TotalMilliseconds, 900, 1100);
            var composition = new MediaComposition();
            composition.Clips.Add(clip);
            using var thumb = await composition.GetThumbnailAsync(TimeSpan.FromMilliseconds(300), w & ~1, h & ~1, VideoFramePrecision.NearestFrame);
            using var bitmap = new Bitmap(thumb.AsStream());
            var top = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 4);
            var bottom = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height * 3 / 4);
            Assert.True(top.R > 180 && top.B < 80, $"top should be red, was {top}");
            Assert.True(bottom.B > 180 && bottom.R < 80, $"bottom should be blue, was {bottom}");
        }
        finally { File.Delete(path); }
    }
}
