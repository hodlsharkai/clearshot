namespace ClearShot.Capture;

internal readonly record struct Rgb8(byte R, byte G, byte B);

/// <summary>
/// Converts scRGB (linear, Rec.709 primaries, 1.0 = 80 nits) to 8-bit sRGB.
/// Windows' SDR white maps to 1.0, so anything that looked like normal SDR on screen
/// comes out unchanged. Highlights above a knee roll off smoothly up to <c>highlightMax</c>
/// (relative to SDR white) instead of clipping.
/// </summary>
internal sealed class HdrToneMapper
{
    public const float Knee = 0.8f;
    private const int LutSize = 1 << 14;
    private static readonly byte[] SrgbLut = BuildSrgbLut();

    private readonly float _scale;
    private readonly float _max;
    private readonly float _slope;
    private readonly bool _rollOff;

    public HdrToneMapper(float sdrWhiteScRgb, float highlightMax)
    {
        _scale = 1f / MathF.Max(sdrWhiteScRgb, 0.01f);
        _rollOff = highlightMax > 1.05f;
        _max = MathF.Min(highlightMax, 64f);
        // Slope of the roll-off curve at the knee, chosen so the curve joins the straight line smoothly.
        _slope = (_max - Knee) / (1f - Knee);
    }

    public Rgb8 Map(float r, float g, float b)
    {
        r = Clean(r) * _scale;
        g = Clean(g) * _scale;
        b = Clean(b) * _scale;

        if (_rollOff)
        {
            float lum = Luminance(r, g, b);
            if (lum > Knee)
            {
                float t = MathF.Min((lum - Knee) / (_max - Knee), 1f);
                float curved = _slope * t / (1f + (_slope - 1f) * t);
                float mapped = Knee + (1f - Knee) * curved;
                float s = mapped / lum;
                r *= s;
                g *= s;
                b *= s;
            }
        }

        return new Rgb8(Encode(r), Encode(g), Encode(b));
    }

    public static float Luminance(float r, float g, float b) => 0.2126f * r + 0.7152f * g + 0.0722f * b;

    /// <summary>Value at fraction <paramref name="p"/> (0..1) of the sorted samples. Copies the input.</summary>
    public static float PercentileOf(float[] samples, double p)
    {
        if (samples.Length == 0) return 0f;
        var sorted = (float[])samples.Clone();
        Array.Sort(sorted);
        int index = (int)Math.Floor(Math.Clamp(p, 0, 1) * (sorted.Length - 1));
        return sorted[index];
    }

    private static float Clean(float v) => float.IsNaN(v) || v < 0f ? 0f : v;

    private static byte Encode(float linear)
    {
        if (linear >= 1f) return 255;
        return SrgbLut[(int)(linear * (LutSize - 1) + 0.5f)];
    }

    private static byte[] BuildSrgbLut()
    {
        var lut = new byte[LutSize];
        for (int i = 0; i < LutSize; i++)
        {
            double x = (double)i / (LutSize - 1);
            double s = x <= 0.0031308 ? 12.92 * x : 1.055 * Math.Pow(x, 1 / 2.4) - 0.055;
            lut[i] = (byte)Math.Clamp(Math.Round(s * 255), 0, 255);
        }
        return lut;
    }
}
