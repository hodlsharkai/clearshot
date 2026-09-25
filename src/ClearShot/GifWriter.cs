using System.Buffers.Binary;
using ClearShot.Capture;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors.Quantization;
using ISImage = SixLabors.ImageSharp.Image;
using ISRectangle = SixLabors.ImageSharp.Rectangle;

namespace ClearShot;

/// <summary>
/// ClearShot's GIF encoder, built for smooth, stable animation from screen and game footage:
/// <list type="bullet">
/// <item>Only pixels that really changed since they were last drawn are redrawn; the rest are transparent, so
/// the previous frame shows through. Still areas can't flicker, and files shrink.</item>
/// <item>Each frame gets its own 255-colour palette, chosen from just the pixels being redrawn (one entry is
/// kept for transparency).</item>
/// <item>Redrawn pixels are error-diffusion dithered (Floyd–Steinberg, serpentine) so gradients and near-black
/// shading blend smoothly instead of breaking into blotches. The palette is chosen with dark shades stretched
/// apart, so near-black gets enough of the colours.</item>
/// </list>
/// Palettes come from ImageSharp's Wu quantiser; dithering, frame differencing and LZW are done here.
/// </summary>
internal static class GifWriter
{
    /// <summary>A pixel counts as changed once any channel moves more than this from what was last drawn.</summary>
    internal const int ChangeThreshold = 3;

    // Tuned on real dark game footage (25/09) against gifski (max quality) and FFmpeg, judged as seen on a normal
    // screen and in Windows HDR mode: ahead of FFmpeg, close to gifski overall, fewest blotches, smallest files.
    private const float DitherStrength = 0.85f;
    private const int CacheBits = 7;
    private const double PaletteGamma = 0.6;

    // Tuned for viewing in Windows HDR mode too, where near-black is shown much brighter than on a normal
    // screen and dither grain there becomes visible (25/09). Settable for the benchmark harness.
    /// <summary>Dither strength at black, ramping up to full by <see cref="DarkRampEnd"/> (the palette's fine
    /// dark steps need little help there).</summary>
    internal static float DarkDither = 0.3f;
    internal static int DarkRampEnd = 48;
    /// <summary>Dither only where it's needed: it fades out as local contrast rises past this many levels,
    /// because detail already hides banding and dithering there only adds grain.</summary>
    internal static float BusyScale = 3f;
    /// <summary>k-means passes that tighten the Wu palette, so less dithering is needed.</summary>
    internal static int RefinePasses = 4;
    private const byte Transparent = 255;

    public static void Save(Recording recording, string path)
    {
        const float ditherStrength = DitherStrength;
        int w = recording.Width, h = recording.Height, n = recording.Frames.Count;

        // Pass 1, in order: decide which pixels each frame redraws, tracking what's on screen for every pixel.
        var shown = new byte[w * h * 3];
        var masks = new ulong[n][];
        var bounds = new System.Drawing.Rectangle[n];
        var keep = new bool[n];
        var durations = new List<int>();
        for (int i = 0; i < n; i++)
        {
            var src = recording.Frames[i].Bgra;
            var mask = new ulong[(w * h + 63) / 64];
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int p = y * w + x, s = p * 4, d = p * 3;
                bool changed = i == 0
                    || Math.Abs(src[s] - shown[d]) > ChangeThreshold
                    || Math.Abs(src[s + 1] - shown[d + 1]) > ChangeThreshold
                    || Math.Abs(src[s + 2] - shown[d + 2]) > ChangeThreshold;
                if (!changed) continue;
                mask[p >> 6] |= 1UL << (p & 63);
                shown[d] = src[s]; shown[d + 1] = src[s + 1]; shown[d + 2] = src[s + 2];
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            if (maxX < 0)
            {
                // Nothing changed: the previous frame just stays up longer.
                durations[^1] += recording.Frames[i].DurationMs;
                continue;
            }
            keep[i] = true;
            masks[i] = mask;
            bounds[i] = System.Drawing.Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
            durations.Add(recording.Frames[i].DurationMs);
        }

        // Pass 2, in parallel: palette, dithering and compression for each frame that draws something.
        var order = Enumerable.Range(0, n).Where(i => keep[i]).ToArray();
        var blocks = new byte[order.Length][];
        Parallel.For(0, order.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, k =>
        {
            int i = order[k];
            blocks[k] = EncodeFrame(recording.Frames[i].Bgra, w, masks[i], bounds[i], ditherStrength, first: k == 0);
            masks[i] = null!;
        });
        foreach (var frame in recording.Frames) frame.Release();

        var delays = GifMaker.CentisecondDelays(durations.ToArray());
        using var file = File.Create(path);
        using var output = new BufferedStream(file, 1 << 20);
        WriteHeader(output, w, h);
        for (int k = 0; k < blocks.Length; k++)
        {
            // Graphic control: keep the previous frame (disposal 1), delay, transparent index.
            output.Write([0x21, 0xF9, 0x04, (1 << 2) | 1, (byte)delays[k], (byte)(delays[k] >> 8), Transparent, 0x00]);
            output.Write(blocks[k]);
        }
        output.WriteByte(0x3B);
    }

    private static void WriteHeader(Stream output, int w, int h)
    {
        output.Write("GIF89a"u8);
        Span<byte> screen = stackalloc byte[7];
        BinaryPrimitives.WriteUInt16LittleEndian(screen, (ushort)w);
        BinaryPrimitives.WriteUInt16LittleEndian(screen[2..], (ushort)h);
        screen[4] = 0x70; // no global colour table, 8-bit colour resolution
        output.Write(screen);
        // Loop forever.
        output.Write([0x21, 0xFF, 0x0B, .. "NETSCAPE2.0"u8, 0x03, 0x01, 0x00, 0x00, 0x00]);
    }

    private static bool IsSet(ulong[] mask, int p) => (mask[p >> 6] & (1UL << (p & 63))) != 0;

    private static byte[] EncodeFrame(byte[] src, int stride, ulong[] mask, System.Drawing.Rectangle box, float ditherStrength, bool first)
    {
        var palette = BuildPalette(src, stride, mask, box);
        var indices = Dither(src, stride, mask, box, palette, ditherStrength);

        using var ms = new MemoryStream();
        Span<byte> desc = stackalloc byte[10];
        desc[0] = 0x2C;
        BinaryPrimitives.WriteUInt16LittleEndian(desc[1..], (ushort)box.X);
        BinaryPrimitives.WriteUInt16LittleEndian(desc[3..], (ushort)box.Y);
        BinaryPrimitives.WriteUInt16LittleEndian(desc[5..], (ushort)box.Width);
        BinaryPrimitives.WriteUInt16LittleEndian(desc[7..], (ushort)box.Height);
        desc[9] = 0x80 | 7; // local colour table of 256 entries
        ms.Write(desc);
        var table = new byte[256 * 3];
        for (int c = 0; c < palette.Length; c++)
        {
            table[c * 3] = palette[c].R; table[c * 3 + 1] = palette[c].G; table[c * 3 + 2] = palette[c].B;
        }
        ms.Write(table);
        Lzw.Encode(indices, ms);
        return ms.ToArray();
    }

    /// <summary>Wu palette of up to 255 colours from only the pixels this frame redraws.</summary>
    private static Rgba32[] BuildPalette(byte[] src, int stride, ulong[] mask, System.Drawing.Rectangle box)
    {
        int count = 0;
        for (int y = box.Top; y < box.Bottom; y++)
            for (int x = box.Left; x < box.Right; x++)
                if (IsSet(mask, y * stride + x)) count++;
        int side = (int)Math.Ceiling(Math.Sqrt(count));
        using var sample = new Image<Rgba32>(side, Math.Max(1, (count + side - 1) / side));
        int k = 0;
        sample.ProcessPixelRows(rows =>
        {
            for (int y = box.Top; y < box.Bottom; y++)
            for (int x = box.Left; x < box.Right; x++)
            {
                int p = y * stride + x;
                if (!IsSet(mask, p)) continue;
                rows.GetRowSpan(k / side)[k % side] = new Rgba32(Warp[src[p * 4 + 2]], Warp[src[p * 4 + 1]], Warp[src[p * 4]], 255);
                k++;
            }
            // Pad the last row with a copy of the first sample so it doesn't add a false colour.
            var fill = rows.GetRowSpan(0)[0];
            for (; k < side * rows.Height; k++) rows.GetRowSpan(k / side)[k % side] = fill;
        });
        var quantizer = new WuQuantizer(new QuantizerOptions { Dither = null, MaxColors = 254 });
        using var frameQuantizer = quantizer.CreatePixelSpecificQuantizer<Rgba32>(Configuration.Default);
        using var indexed = frameQuantizer.BuildPaletteAndQuantizeFrame(sample.Frames.RootFrame, sample.Bounds);
        var warped = indexed.Palette.ToArray();
        if (RefinePasses > 0) Refine(warped, sample, RefinePasses);
        var palette = warped.Select(c => new Rgba32(Unwarp[c.R], Unwarp[c.G], Unwarp[c.B], 255)).ToList();
        // Always offer true black. Otherwise black gets averaged in with nearby dark shades and comes out grey.
        if (!palette.Contains(new Rgba32(0, 0, 0, 255))) palette.Add(new Rgba32(0, 0, 0, 255));
        return palette.ToArray();
    }

    /// <summary>
    /// A few k-means passes over a sample of the pixels: each colour moves to the average of the pixels
    /// nearest it. Wu's split-the-box palette is a good start; this tightens it so less dithering is needed.
    /// </summary>
    private static void Refine(Rgba32[] palette, Image<Rgba32> sample, int passes)
    {
        var pixels = new List<Rgba32>();
        int step = Math.Max(1, sample.Width * sample.Height / 60000);
        sample.ProcessPixelRows(rows =>
        {
            int k = 0;
            for (int y = 0; y < rows.Height; y++)
                foreach (var px in rows.GetRowSpan(y))
                    if (k++ % step == 0) pixels.Add(px);
        });
        var sums = new long[palette.Length * 3];
        var counts = new int[palette.Length];
        for (int pass = 0; pass < passes; pass++)
        {
            Array.Clear(sums);
            Array.Clear(counts);
            foreach (var px in pixels)
            {
                int best = 0, bestDist = int.MaxValue;
                for (int i = 0; i < palette.Length; i++)
                {
                    int dr = palette[i].R - px.R, dg = palette[i].G - px.G, db = palette[i].B - px.B;
                    int dist = 2 * dr * dr + 4 * dg * dg + 3 * db * db;
                    if (dist < bestDist) { bestDist = dist; best = i; }
                }
                sums[best * 3] += px.R; sums[best * 3 + 1] += px.G; sums[best * 3 + 2] += px.B;
                counts[best]++;
            }
            for (int i = 0; i < palette.Length; i++)
                if (counts[i] > 0)
                    palette[i] = new Rgba32((byte)(sums[i * 3] / counts[i]), (byte)(sums[i * 3 + 1] / counts[i]), (byte)(sums[i * 3 + 2] / counts[i]), 255);
        }
    }

    // The palette is chosen in a space that stretches dark shades apart, so near-black gets more of the 255
    // colours; it's then mapped back. (Gamma 1 = no change.)
    private static readonly byte[] Warp = Table(v => 255 * Math.Pow(v / 255, PaletteGamma));
    private static readonly byte[] Unwarp = Table(v => 255 * Math.Pow(v / 255, 1 / PaletteGamma));

    private static byte[] Table(Func<double, double> f)
    {
        var t = new byte[256];
        for (int i = 0; i < 256; i++) t[i] = (byte)Math.Clamp(Math.Round(f(i)), 0, 255);
        return t;
    }

    /// <summary>
    /// Floyd–Steinberg in serpentine order over the redrawn pixels; everything else becomes transparent.
    /// Error is only carried between redrawn pixels, and is clamped so a single outlier can't smear.
    /// </summary>
    private static byte[] Dither(byte[] src, int stride, ulong[] mask, System.Drawing.Rectangle box, Rgba32[] palette, float strength)
    {
        int bw = box.Width, bh = box.Height;
        var indices = new byte[bw * bh];
        var nearest = new NearestColour(palette);
        var errCur = new float[(bw + 2) * 3];
        var errNext = new float[(bw + 2) * 3];
        for (int y = 0; y < bh; y++)
        {
            bool leftToRight = (y & 1) == 0;
            Array.Clear(errNext);
            for (int step = 0; step < bw; step++)
            {
                int x = leftToRight ? step : bw - 1 - step;
                int p = (box.Top + y) * stride + box.Left + x;
                int o = y * bw + x;
                if (!IsSet(mask, p))
                {
                    indices[o] = Transparent;
                    continue;
                }
                int e = (x + 1) * 3;
                float b = src[p * 4] + errCur[e], g = src[p * 4 + 1] + errCur[e + 1], r = src[p * 4 + 2] + errCur[e + 2];
                int ci = nearest.Find(Clamp(r), Clamp(g), Clamp(b));
                indices[o] = (byte)ci;
                var c = palette[ci];
                float level = Math.Max(r, Math.Max(g, b));
                float k = level >= DarkRampEnd ? strength : DarkDither + (strength - DarkDither) * Math.Max(0, level) / DarkRampEnd;
                if (BusyScale > 0) k *= Math.Max(0.2f, 1f - Busyness(src, stride, box, p) / BusyScale);
                float er = Limit((r - c.R) * k), eg = Limit((g - c.G) * k), eb = Limit((b - c.B) * k);
                int dir = leftToRight ? 1 : -1;
                int ahead = (x + 1 + dir) * 3, behind = (x + 1 - dir) * 3;
                Spread(errCur, ahead, er, eg, eb, 7f / 16);
                Spread(errNext, behind, er, eg, eb, 3f / 16);
                Spread(errNext, e, er, eg, eb, 5f / 16);
                Spread(errNext, ahead, er, eg, eb, 1f / 16);
            }
            (errCur, errNext) = (errNext, errCur);
        }
        return indices;

        // Largest luma step to the four neighbours: high on edges and busy texture, low in smooth gradients.
        static float Busyness(byte[] src, int stride, System.Drawing.Rectangle box, int p)
        {
            int x = p % stride, y = p / stride;
            float here = Luma(src, p), most = 0;
            if (x > 0) most = Math.Max(most, Math.Abs(Luma(src, p - 1) - here));
            if (x + 1 < stride) most = Math.Max(most, Math.Abs(Luma(src, p + 1) - here));
            if (y > 0) most = Math.Max(most, Math.Abs(Luma(src, p - stride) - here));
            if ((p + stride) * 4 < src.Length) most = Math.Max(most, Math.Abs(Luma(src, p + stride) - here));
            return most;
        }
        static float Luma(byte[] s, int p) => 0.0722f * s[p * 4] + 0.7152f * s[p * 4 + 1] + 0.2126f * s[p * 4 + 2];

        static void Spread(float[] row, int at, float r, float g, float b, float share)
        {
            row[at] += b * share; row[at + 1] += g * share; row[at + 2] += r * share;
        }
        static int Clamp(float v) => v < 0 ? 0 : v > 255 ? 255 : (int)(v + 0.5f);
        static float Limit(float v) => v < -32 ? -32 : v > 32 ? 32 : v;
    }

    /// <summary>
    /// Nearest palette entry. Dark colours (every channel under 64), where single levels show, are matched
    /// exactly; the rest through a cache of 7 bits per channel. Both caches fill lazily.
    /// </summary>
    private sealed class NearestColour(Rgba32[] palette)
    {
        private const int _bits = CacheBits;
        private readonly short[] _cache = CreateCache(CacheBits);
        private readonly short[] _dark = CreateCache(6);

        private static short[] CreateCache(int bits)
        {
            var cache = new short[1 << (bits * 3)];
            Array.Fill(cache, (short)-1);
            return cache;
        }

        public int Find(int r, int g, int b)
        {
            if ((r | g | b) < 64)
            {
                int darkKey = r << 12 | g << 6 | b;
                int known = _dark[darkKey];
                if (known >= 0) return known;
                return _dark[darkKey] = (short)Search(r, g, b);
            }
            int shift = 8 - _bits;
            int key = (r >> shift) << (_bits * 2) | (g >> shift) << _bits | (b >> shift);
            int hit = _cache[key];
            if (hit >= 0) return hit;
            // Search from the centre of the cache cell, weighting green most as the eye does.
            int half = (1 << shift) >> 1, mask = ~((1 << shift) - 1);
            int cr = (r & mask) + half, cg = (g & mask) + half, cb = (b & mask) + half;
            int found = Search(cr, cg, cb);
            _cache[key] = (short)found;
            return found;
        }

        private int Search(int cr, int cg, int cb)
        {
            int best = 0, bestDist = int.MaxValue;
            for (int i = 0; i < palette.Length; i++)
            {
                int dr = palette[i].R - cr, dg = palette[i].G - cg, db = palette[i].B - cb;
                int dist = 2 * dr * dr + 4 * dg * dg + 3 * db * db;
                if (dist < bestDist) { bestDist = dist; best = i; }
            }
            return best;
        }
    }

    /// <summary>GIF's variable-width LZW, 8-bit codes, written as 255-byte sub-blocks.</summary>
    internal static class Lzw
    {
        public static void Encode(byte[] pixels, Stream output)
        {
            const int minCodeSize = 8, clear = 1 << minCodeSize, end = clear + 1, maxCode = 4095;
            output.WriteByte(minCodeSize);
            var packer = new BitPacker(output);
            var table = new Dictionary<int, int>(8192);
            int codeSize = minCodeSize + 1, next = end + 1;
            packer.Write(clear, codeSize);
            if (pixels.Length == 0) { packer.Write(end, codeSize); packer.Flush(); output.WriteByte(0); return; }

            int prefix = pixels[0];
            for (int i = 1; i < pixels.Length; i++)
            {
                int key = (prefix << 8) | pixels[i];
                if (table.TryGetValue(key, out int code)) { prefix = code; continue; }
                packer.Write(prefix, codeSize);
                if (next <= maxCode)
                {
                    table[key] = next++;
                    if (next > (1 << codeSize) && codeSize < 12) codeSize++;
                }
                else
                {
                    packer.Write(clear, codeSize);
                    table.Clear();
                    codeSize = minCodeSize + 1;
                    next = end + 1;
                }
                prefix = pixels[i];
            }
            packer.Write(prefix, codeSize);
            packer.Write(end, codeSize);
            packer.Flush();
            output.WriteByte(0); // block terminator
        }

        private sealed class BitPacker(Stream output)
        {
            private readonly byte[] _block = new byte[255];
            private int _blockLength, _bits;
            private uint _buffer;

            public void Write(int code, int size)
            {
                _buffer |= (uint)code << _bits;
                _bits += size;
                while (_bits >= 8) { Push((byte)_buffer); _buffer >>= 8; _bits -= 8; }
            }

            public void Flush()
            {
                if (_bits > 0) { Push((byte)_buffer); _buffer = 0; _bits = 0; }
                if (_blockLength > 0) { output.WriteByte((byte)_blockLength); output.Write(_block, 0, _blockLength); _blockLength = 0; }
            }

            private void Push(byte b)
            {
                _block[_blockLength++] = b;
                if (_blockLength == 255) { output.WriteByte(255); output.Write(_block, 0, 255); _blockLength = 0; }
            }
        }
    }
}
