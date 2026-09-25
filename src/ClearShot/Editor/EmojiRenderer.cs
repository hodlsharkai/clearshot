using System.Drawing.Imaging;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Vortice.WIC;

namespace ClearShot.Editor;

/// <summary>
/// Draws emoji in full colour. GDI+ (what the rest of the editor uses) only draws emoji in black and white, so these
/// go through Direct2D and DirectWrite with colour fonts switched on, using Windows' own Segoe UI Emoji.
/// Each emoji is drawn at the exact size it's shown, so it stays sharp at any size.
/// </summary>
internal static class EmojiRenderer
{
    public static readonly string[] Quick =
    [
        "😀", "😂", "🤣", "😍", "😎", "🤔", "😮", "😭", "😡", "🥳", "👍", "👎", "👏", "🙏", "💪", "👀",
        "🔥", "💯", "✅", "❌", "⚠️", "❓", "❗", "💡", "⭐", "❤️", "💀", "🎉", "🚀", "💰", "📌", "👉",
    ];

    private static readonly object Gate = new();
    private static ID2D1Factory? _d2d;
    private static IDWriteFactory? _dwrite;
    private static IWICImagingFactory? _wic;
    private static readonly Dictionary<(string, int), Bitmap> Cache = [];

    /// <summary>The emoji on a transparent square about 1.3 × <paramref name="size"/> across. Cached; don't dispose it.</summary>
    public static Bitmap Render(string emoji, float size)
    {
        int px = Math.Clamp((int)Math.Round(size), 6, 2048);
        lock (Gate)
        {
            if (Cache.TryGetValue((emoji, px), out var cached)) return cached;
            if (Cache.Count > 64)
            {
                foreach (var old in Cache.Values) old.Dispose();
                Cache.Clear();
            }
            var bitmap = Draw(emoji, px);
            Cache[(emoji, px)] = bitmap;
            return bitmap;
        }
    }

    public static int BoxFor(float size) => (int)Math.Ceiling(Math.Clamp(size, 6, 2048) * 1.3f);

    private static Bitmap Draw(string emoji, int px)
    {
        _d2d ??= D2D1.D2D1CreateFactory<ID2D1Factory>();
        _dwrite ??= DWrite.DWriteCreateFactory<IDWriteFactory>();
        _wic ??= new IWICImagingFactory();
        int box = BoxFor(px);
        using var target = _wic.CreateBitmap((uint)box, (uint)box, Vortice.WIC.PixelFormat.Format32bppPBGRA, BitmapCreateCacheOption.CacheOnLoad);
        var props = new RenderTargetProperties(new Vortice.DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied));
        using (var rt = _d2d.CreateWicBitmapRenderTarget(target, props))
        using (var format = _dwrite.CreateTextFormat("Segoe UI Emoji", null, FontWeight.Normal, Vortice.DirectWrite.FontStyle.Normal, FontStretch.Normal, px, "en-gb"))
        using (var brush = rt.CreateSolidColorBrush(new Color4(0, 0, 0, 1)))
        {
            format.TextAlignment = TextAlignment.Center;
            format.ParagraphAlignment = ParagraphAlignment.Center;
            rt.BeginDraw();
            rt.Clear(new Color4(0, 0, 0, 0));
            rt.DrawText(emoji, format, new Rect(0, 0, box, box), brush, DrawTextOptions.EnableColorFont);
            rt.EndDraw();
        }
        var bitmap = new Bitmap(box, box, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, box, box), ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try
        {
            target.CopyPixels(new RectI(0, 0, box, box), (uint)data.Stride, (uint)(data.Stride * box), data.Scan0);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }
}
