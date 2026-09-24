using System.Buffers.Binary;
using System.IO.Compression;
using ClearShot.Capture;
using Vortice.WIC;

namespace ClearShot.Tests;

public class HdrWriterTests
{
    [Theory]
    [InlineData(0, 0.0, 0.0001)]
    [InlineData(100, 0.5081, 0.0005)]
    [InlineData(1000, 0.7518, 0.0005)]
    [InlineData(10000, 1.0, 0.00001)]
    public void Pq_matches_published_values(double nits, double expected, double tolerance)
    {
        Assert.InRange(HdrWriters.PqEncode(nits), expected - tolerance, expected + tolerance);
    }

    [Fact]
    public void Sdr_white_grey_stays_grey_in_rec2020()
    {
        var (r, g, b) = HdrWriters.ScRgbToPq(2.5f, 2.5f, 2.5f); // 200 nits
        Assert.InRange(Math.Abs(r - g), 0, 40);
        Assert.InRange(Math.Abs(g - b), 0, 40);
    }

    [Fact]
    public void Crop_takes_the_right_pixels()
    {
        var frame = TestFrame(4, 3);
        var crop = frame.Crop(new Rectangle(1, 1, 2, 2));
        Assert.Equal(2, crop.Width);
        Assert.Equal(2, crop.Height);
        Assert.Equal(frame.Pixels[(1 * 4 + 1) * 4], crop.Pixels[0]);
        Assert.Equal(frame.Pixels[(2 * 4 + 2) * 4], crop.Pixels[(1 * 2 + 1) * 4]);
    }

    [Fact]
    public void Hdr_png_is_valid_and_labelled_hdr10()
    {
        var frame = TestFrame(5, 3);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        try
        {
            HdrWriters.WritePqPng(frame, path);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, bytes[..8]);

            var chunks = ReadChunks(bytes);
            Assert.Equal(["IHDR", "cICP", "IDAT", "IEND"], chunks.Select(c => c.Type));
            Assert.Equal(new byte[] { 9, 16, 0, 1 }, chunks[1].Data);
            Assert.Equal(16, chunks[0].Data[8]);

            // Decode the pixel data and check the first pixel round-trips.
            using var zlib = new ZLibStream(new MemoryStream(chunks[2].Data), CompressionMode.Decompress);
            using var raw = new MemoryStream();
            zlib.CopyTo(raw);
            Assert.Equal(3 * (1 + 5 * 6), raw.Length);
            var first = raw.ToArray().AsSpan(1);
            var expected = HdrWriters.ScRgbToPq((float)frame.Pixels[0], (float)frame.Pixels[1], (float)frame.Pixels[2]);
            Assert.Equal(expected.R, BinaryPrimitives.ReadUInt16BigEndian(first));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Jxr_round_trips_through_windows_imaging()
    {
        var frame = TestFrame(8, 6);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jxr");
        try
        {
            HdrWriters.WriteJxr(frame, path);
            using var factory = new IWICImagingFactory();
            using var decoder = factory.CreateDecoderFromFileName(path);
            using var decoded = decoder.GetFrame(0);
            Assert.Equal(8u, (uint)decoded.Size.Width);
            Assert.Equal(6u, (uint)decoded.Size.Height);
            Assert.True(decoded.PixelFormat == PixelFormat.Format64bppRGBAHalf || decoded.PixelFormat == PixelFormat.Format64bppRGBHalf,
                $"decoded as {decoded.PixelFormat}");
        }
        finally { File.Delete(path); }
    }

    private static HdrFrame TestFrame(int w, int h)
    {
        var px = new Half[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            px[i * 4] = (Half)(i * 0.5f);
            px[i * 4 + 1] = (Half)(i * 0.25f);
            px[i * 4 + 2] = (Half)1f;
            px[i * 4 + 3] = (Half)1f;
        }
        return new HdrFrame(w, h, px);
    }

    private static List<(string Type, byte[] Data)> ReadChunks(byte[] png)
    {
        var chunks = new List<(string, byte[])>();
        int pos = 8;
        while (pos < png.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
            var type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            var data = png.AsSpan(pos + 8, len).ToArray();
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos + 8 + len));
            uint actual = HdrWriters.Crc32.Update(0xFFFFFFFFu, png.AsSpan(pos + 4, 4 + len)) ^ 0xFFFFFFFFu;
            Assert.Equal(crc, actual);
            chunks.Add((type, data));
            pos += 12 + len;
        }
        return chunks;
    }
}
