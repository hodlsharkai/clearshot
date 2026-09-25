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

    // Each thread gets its own Direct2D, DirectWrite and WIC objects, so the background preparation of the emoji
    // list never makes the editor wait (they're not shared, so nothing needs locking).
    [ThreadStatic] private static ID2D1Factory? _d2d;
    [ThreadStatic] private static IDWriteFactory? _dwrite;
    [ThreadStatic] private static IWICImagingFactory? _wic;
    [ThreadStatic] private static IDWriteTextFormat? _measure;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, int), Bitmap> Cache = new();

    /// <summary>The emoji on a transparent square about 1.3 × <paramref name="size"/> across. Cached; don't dispose it.</summary>
    public static Bitmap Render(string emoji, float size)
    {
        int px = Math.Clamp((int)Math.Round(size), 6, 2048);
        if (Cache.TryGetValue((emoji, px), out var cached)) return cached;
        // Stickers being resized leave old sizes behind; don't let them pile up.
        if (Cache.Count > 64) Cache.Clear();
        return Cache.GetOrAdd((emoji, px), k => Draw(k.Item1, k.Item2));
    }

    /// <summary>A fresh picture the caller owns (the picker keeps its own set at its own size).</summary>
    public static Bitmap RenderUncached(string emoji, float size)
    {
        return Draw(emoji, Math.Clamp((int)Math.Round(size), 6, 2048));
    }

    /// <summary>
    /// True if Windows' emoji font draws this as one picture. Some combinations (certain couples and families) aren't in
    /// the font and come out as separate pieces side by side; the picker leaves those out.
    /// </summary>
    public static bool DrawsAsOne(string emoji)
    {
        _dwrite ??= DWrite.DWriteCreateFactory<IDWriteFactory>();
        _measure ??= _dwrite.CreateTextFormat("Segoe UI Emoji", null, FontWeight.Normal, Vortice.DirectWrite.FontStyle.Normal, FontStretch.Normal, 32, "en-gb");
        using var layout = _dwrite.CreateTextLayout(emoji, _measure, 1000, 100);
        if (layout.Metrics.Width > 32 * 1.5f) return false;
        // A combination the font really has is one glyph; one it doesn't is drawn as several glyphs squeezed
        // together (two faces and a heart). Count the glyphs DirectWrite would draw.
        var counter = new GlyphCounter();
        layout.Draw(IntPtr.Zero, counter, 0, 0);
        return counter.Glyphs <= 1;
    }

    /// <summary>Counts the glyphs a layout draws, without drawing anything.</summary>
    private sealed class GlyphCounter : TextRendererBase
    {
        public int Glyphs;

        public override void DrawGlyphRun(IntPtr clientDrawingContext, float baselineOriginX, float baselineOriginY,
            Vortice.DCommon.MeasuringMode measuringMode, GlyphRun glyphRun, GlyphRunDescription glyphRunDescription, SharpGen.Runtime.IUnknown clientDrawingEffect)
        {
            Glyphs += glyphRun.Indices?.Length ?? 0;
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
