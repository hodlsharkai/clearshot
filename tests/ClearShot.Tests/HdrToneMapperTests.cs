using ClearShot.Capture;

namespace ClearShot.Tests;

public class HdrToneMapperTests
{
    // scRGB: 1.0 = 80 nits. With SDR white at 240 nits, SDR white is 3.0 in scRGB.
    private const float SdrWhiteScRgb = 3.0f;

    [Fact]
    public void Black_stays_black()
    {
        var mapper = new HdrToneMapper(SdrWhiteScRgb, highlightMax: 4f);
        Assert.Equal((byte)0, mapper.Map(0f, 0f, 0f).R);
    }

    [Fact]
    public void Sdr_white_maps_to_near_white()
    {
        // With highlight headroom the knee sits below white, so SDR white lands close to, not at, 255.
        var mapper = new HdrToneMapper(SdrWhiteScRgb, highlightMax: 1f);
        var px = mapper.Map(SdrWhiteScRgb, SdrWhiteScRgb, SdrWhiteScRgb);
        Assert.Equal((byte)255, px.R);
        Assert.Equal((byte)255, px.G);
        Assert.Equal((byte)255, px.B);
    }

    [Fact]
    public void Midtones_match_plain_srgb_when_below_knee()
    {
        var mapper = new HdrToneMapper(SdrWhiteScRgb, highlightMax: 4f);
        // Linear 0.18 (relative to SDR white) is sRGB 118.
        var v = 0.18f * SdrWhiteScRgb;
        Assert.InRange(mapper.Map(v, v, v).R, (byte)117, (byte)119);
    }

    [Fact]
    public void Highlights_roll_off_instead_of_clipping_early()
    {
        var mapper = new HdrToneMapper(SdrWhiteScRgb, highlightMax: 4f);
        byte at1 = mapper.Map(SdrWhiteScRgb, SdrWhiteScRgb, SdrWhiteScRgb).R;
        byte at2 = mapper.Map(2 * SdrWhiteScRgb, 2 * SdrWhiteScRgb, 2 * SdrWhiteScRgb).R;
        byte at4 = mapper.Map(4 * SdrWhiteScRgb, 4 * SdrWhiteScRgb, 4 * SdrWhiteScRgb).R;
        Assert.True(at1 < at2, $"expected {at1} < {at2}");
        Assert.True(at2 < at4, $"expected {at2} < {at4}");
        Assert.Equal((byte)255, at4);
    }

    [Fact]
    public void Output_is_monotonic()
    {
        var mapper = new HdrToneMapper(SdrWhiteScRgb, highlightMax: 6f);
        byte last = 0;
        for (float v = 0; v < 8 * SdrWhiteScRgb; v += 0.01f)
        {
            byte r = mapper.Map(v, v, v).R;
            Assert.True(r >= last, $"not monotonic at {v}");
            last = r;
        }
    }

    [Fact]
    public void Negative_wide_gamut_values_are_clamped()
    {
        var mapper = new HdrToneMapper(SdrWhiteScRgb, highlightMax: 4f);
        var px = mapper.Map(-0.5f, 1f, float.NaN);
        Assert.Equal((byte)0, px.R);
        Assert.Equal((byte)0, px.B);
    }

    [Fact]
    public void Hue_is_preserved_for_bright_saturated_colour()
    {
        var mapper = new HdrToneMapper(SdrWhiteScRgb, highlightMax: 4f);
        var px = mapper.Map(3 * SdrWhiteScRgb, 0.3f * SdrWhiteScRgb, 0f);
        Assert.True(px.R > px.G && px.G > px.B, $"got {px.R},{px.G},{px.B}");
    }

    [Fact]
    public void Percentile_luminance_ignores_a_few_hot_pixels()
    {
        var lum = new float[1000];
        Array.Fill(lum, 1f);
        lum[0] = 50f;
        lum[1] = 50f;
        Assert.InRange(HdrToneMapper.PercentileOf(lum, 0.995), 0.99f, 1.01f);
    }
}
