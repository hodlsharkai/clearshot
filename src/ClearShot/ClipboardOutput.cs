using System.Drawing.Imaging;

namespace ClearShot;

internal static class ClipboardOutput
{
    public static byte[] EncodePng(Bitmap image)
    {
        using var ms = new MemoryStream();
        image.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>
    /// Puts the file itself on the clipboard, as Explorer's Copy does. Pasting into Discord or a chat then
    /// uploads the actual animated GIF rather than a still picture.
    /// </summary>
    public static void CopyFile(string path)
    {
        var data = new DataObject();
        data.SetFileDropList([path]);
        data.SetData("Preferred DropEffect", false, new MemoryStream(BitConverter.GetBytes(1))); // DROPEFFECT_COPY
        Clipboard.SetDataObject(data, copy: true, retryTimes: 10, retryDelay: 50);
    }

    /// <summary>Puts the image on the clipboard as both a bitmap and a PNG, so it pastes cleanly into any app.</summary>
    public static void Copy(Bitmap image, byte[] png)
    {
        var data = new DataObject();
        data.SetImage(image);
        data.SetData("PNG", false, new MemoryStream(png));
        Clipboard.SetDataObject(data, copy: true, retryTimes: 10, retryDelay: 50);
    }
}
