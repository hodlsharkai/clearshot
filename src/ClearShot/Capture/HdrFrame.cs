namespace ClearShot.Capture;

/// <summary>
/// The untouched HDR pixels of a capture: scRGB (linear, Rec.709 primaries, 1.0 = 80 nits), RGBA half floats.
/// Kept only when an HDR copy is going to be saved, since a 4K frame is about 66 MB.
/// </summary>
internal sealed class HdrFrame(int width, int height, Half[] pixels)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public Half[] Pixels { get; } = pixels;

    public HdrFrame Crop(Rectangle area)
    {
        area.Intersect(new Rectangle(0, 0, Width, Height));
        var cropped = new Half[area.Width * area.Height * 4];
        for (int y = 0; y < area.Height; y++)
            Array.Copy(Pixels, ((area.Y + y) * Width + area.X) * 4, cropped, y * area.Width * 4, area.Width * 4);
        return new HdrFrame(area.Width, area.Height, cropped);
    }
}
