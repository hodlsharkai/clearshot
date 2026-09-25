using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ClearShot.Capture;

namespace ClearShot.Editor;

/// <summary>
/// Puts what was drawn in the editor onto every frame of a GIF. Drawings (text, arrows, emoji, highlighter, steps)
/// are drawn once onto a see-through layer, shrunk to the GIF's size and laid over each frame; the eraser rubs out
/// that layer. Pixelate is worked out on each frame, so it keeps hiding whatever moves underneath.
/// </summary>
internal static class GifAnnotator
{
    /// <param name="drawings">What was drawn, in the pixels of the editor's picture.</param>
    /// <param name="monitorSize">The size of the editor's picture (the monitor).</param>
    /// <param name="area">Where the GIF's area sits in that picture.</param>
    public static void Apply(Recording recording, IReadOnlyList<Annotation> drawings, Size monitorSize, Rectangle area)
    {
        if (drawings.Count == 0) return;
        float sx = (float)recording.Width / area.Width, sy = (float)recording.Height / area.Height;

        // 1. Everything except pixelation, on a transparent layer the size of the picture.
        using var clear = new Bitmap(monitorSize.Width, monitorSize.Height, PixelFormat.Format32bppArgb);
        using var layerDoc = new EditDocument(clear);
        var pixelates = new List<PixelateBox>();
        foreach (var item in drawings)
        {
            switch (item)
            {
                case PixelateBox box:
                    pixelates.Add(new PixelateBox
                    {
                        Start = new PointF((box.Area.Left - area.X) * sx, (box.Area.Top - area.Y) * sy),
                        End = new PointF((box.Area.Right - area.X) * sx, (box.Area.Bottom - area.Y) * sy),
                        BlockSize = Math.Max(4, (int)Math.Round(box.BlockSize * sx)),
                    });
                    break;
                case EraserStroke eraser:
                    // On the layer, erasing means making it see-through again.
                    var onLayer = new EraserStroke { Size = eraser.Size, Original = clear };
                    onLayer.Points.AddRange(eraser.Points);
                    layerDoc.Add(onLayer);
                    break;
                default:
                    layerDoc.Add(item);
                    break;
            }
        }

        // 2. The layer cut to the GIF's area and shrunk to the GIF's size, once.
        using var cut = layerDoc.Render(area);
        using var layer = new Bitmap(recording.Width, recording.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(layer))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(cut, new Rectangle(0, 0, recording.Width, recording.Height));
        }

        // 3. Each frame: pixelate its own pixels, then lay the drawings over it.
        int w = recording.Width, h = recording.Height, rowBytes = w * 4;
        using var frame = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        foreach (var f in recording.Frames)
        {
            Copy(f.Bgra, frame, toBitmap: true, rowBytes);
            using (var g = Graphics.FromImage(frame))
            {
                foreach (var box in pixelates) box.Draw(g, frame);
                g.CompositingMode = CompositingMode.SourceOver;
                g.DrawImageUnscaled(layer, 0, 0);
            }
            Copy(f.Bgra, frame, toBitmap: false, rowBytes);
        }
    }

    private static void Copy(byte[] bgra, Bitmap bitmap, bool toBitmap, int rowBytes)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < bitmap.Height; y++)
            {
                if (toBitmap) Marshal.Copy(bgra, y * rowBytes, data.Scan0 + y * data.Stride, rowBytes);
                else Marshal.Copy(data.Scan0 + y * data.Stride, bgra, y * rowBytes, rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>The picture the editor shows for a GIF: its first frame, blown back up to the recorded area on a black monitor.</summary>
    public static Bitmap EditorPicture(Bitmap firstFrame, Size monitorSize, Rectangle area)
    {
        var picture = new Bitmap(monitorSize.Width, monitorSize.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(picture);
        g.Clear(Color.Black);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(firstFrame, area);
        return picture;
    }
}
