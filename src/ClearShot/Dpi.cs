using System.Runtime.InteropServices;

namespace ClearShot;

internal static class Dpi
{
    /// <summary>Scale factor (1.0 = 100%) of the monitor that contains most of <paramref name="bounds"/>.</summary>
    public static float ScaleFor(Rectangle bounds)
    {
        try
        {
            var rect = new NativeRect { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom };
            var monitor = MonitorFromRect(ref rect, 2);
            if (GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0 && dpiX > 0) return dpiX / 96f;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
        return 1f;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
