using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Vortice.WIC;

namespace ClearShot.Capture;

/// <summary>Saves true HDR copies of a capture.</summary>
internal static class HdrWriters
{
    /// <summary>
    /// JPEG XR in scRGB half floats: the same kind of file Xbox Game Bar saves. Windows Photos shows it in HDR.
    /// Encoded by Windows' own imaging component, so nothing extra ships with ClearShot.
    /// </summary>
    public static void WriteJxr(HdrFrame frame, string path)
    {
        using var factory = new IWICImagingFactory();
        using var file = File.Create(path);
        using var stream = factory.CreateStream(file);
        using var encoder = factory.CreateEncoder(ContainerFormat.Wmp, stream);
        using var frameEncode = encoder.CreateNewFrame(out var options);
        frameEncode.Initialize(options);
        options?.Dispose();
        frameEncode.SetSize((uint)frame.Width, (uint)frame.Height);
        var format = PixelFormat.Format64bppRGBAHalf;
        frameEncode.SetPixelFormat(ref format);
        // The encoder may pick the alpha-less variant; the memory layout is the same either way.
        if (format != PixelFormat.Format64bppRGBAHalf && format != PixelFormat.Format64bppRGBHalf)
            throw new NotSupportedException($"The JPEG XR encoder wanted an unexpected pixel format ({format}).");
        var bytes = MemoryMarshal.AsBytes(frame.Pixels.AsSpan());
        frameEncode.WritePixels((uint)frame.Height, (uint)(frame.Width * 8), bytes.ToArray());
        frameEncode.Commit();
        encoder.Commit();
    }

    /// <summary>
    /// 16-bit PNG in the HDR10 colour space (Rec.2020 primaries, PQ curve), labelled with a cICP chunk
    /// so Chrome and Edge show it in HDR.
    /// </summary>
    public static void WritePqPng(HdrFrame frame, string path)
    {
        int width = frame.Width, height = frame.Height;
        int rowBytes = width * 6;
        var raw = new byte[(long)height * rowBytes];
        Parallel.For(0, height, y =>
        {
            var row = raw.AsSpan(y * rowBytes, rowBytes);
            var src = frame.Pixels.AsSpan(y * width * 4, width * 4);
            for (int x = 0; x < width; x++)
            {
                var (r, g, b) = ScRgbToPq((float)src[x * 4], (float)src[x * 4 + 1], (float)src[x * 4 + 2]);
                BinaryPrimitives.WriteUInt16BigEndian(row[(x * 6)..], r);
                BinaryPrimitives.WriteUInt16BigEndian(row[(x * 6 + 2)..], g);
                BinaryPrimitives.WriteUInt16BigEndian(row[(x * 6 + 4)..], b);
            }
        });

        using var file = File.Create(path);
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 16; // bit depth
        ihdr[9] = 2;  // truecolour RGB
        WriteChunk(file, "IHDR", ihdr);
        // cICP: BT.2020 primaries (9), PQ transfer (16), RGB (0), full range (1).
        WriteChunk(file, "cICP", [9, 16, 0, 1]);

        using (var idat = new MemoryStream())
        {
            using (var zlib = new ZLibStream(idat, CompressionLevel.Fastest, leaveOpen: true))
            {
                // "Up" filter: each row stored as the difference from the row above, which compresses well.
                var filtered = new byte[rowBytes + 1];
                filtered[0] = 2;
                for (int y = 0; y < height; y++)
                {
                    var row = raw.AsSpan(y * rowBytes, rowBytes);
                    if (y == 0) row.CopyTo(filtered.AsSpan(1));
                    else
                    {
                        var above = raw.AsSpan((y - 1) * rowBytes, rowBytes);
                        for (int i = 0; i < rowBytes; i++) filtered[i + 1] = (byte)(row[i] - above[i]);
                    }
                    zlib.Write(filtered);
                }
            }
            WriteChunk(file, "IDAT", idat.GetBuffer().AsSpan(0, (int)idat.Length));
        }
        WriteChunk(file, "IEND", []);
    }

    /// <summary>scRGB linear Rec.709 (1.0 = 80 nits) to 16-bit PQ Rec.2020 code values.</summary>
    public static (ushort R, ushort G, ushort B) ScRgbToPq(float r, float g, float b)
    {
        r = Clean(r); g = Clean(g); b = Clean(b);
        float r2 = 0.6274040f * r + 0.3292820f * g + 0.0433136f * b;
        float g2 = 0.0690970f * r + 0.9195400f * g + 0.0113612f * b;
        float b2 = 0.0163916f * r + 0.0880132f * g + 0.8955950f * b;
        return (Pq16(r2 * 80f), Pq16(g2 * 80f), Pq16(b2 * 80f));
    }

    /// <summary>SMPTE ST 2084 (PQ) inverse EOTF: absolute nits to a 0..1 signal.</summary>
    public static double PqEncode(double nits)
    {
        const double m1 = 2610.0 / 16384, m2 = 2523.0 / 4096 * 128;
        const double c1 = 3424.0 / 4096, c2 = 2413.0 / 4096 * 32, c3 = 2392.0 / 4096 * 32;
        double y = Math.Clamp(nits / 10000.0, 0, 1);
        double p = Math.Pow(y, m1);
        return Math.Pow((c1 + c2 * p) / (1 + c3 * p), m2);
    }

    private static ushort Pq16(float nits) => (ushort)Math.Round(PqEncode(nits) * 65535);

    private static float Clean(float v) => float.IsNaN(v) || v < 0f ? 0f : float.IsInfinity(v) ? 10000f / 80f : v;

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(header, data.Length);
        System.Text.Encoding.ASCII.GetBytes(type, header[4..]);
        output.Write(header);
        output.Write(data);
        uint crc = Crc32.Update(Crc32.Update(0xFFFFFFFFu, header[4..]), data) ^ 0xFFFFFFFFu;
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    internal static class Crc32
    {
        private static readonly uint[] Table = Build();

        public static uint Update(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (var b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        private static uint[] Build()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }
    }
}
