using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace ClearShot.Capture;

/// <param name="Image">The captured monitor, 32-bit, fully opaque, at native resolution.</param>
/// <param name="Bounds">Where that monitor sits on the virtual desktop, in physical pixels.</param>
/// <param name="UsedFallback">True if Desktop Duplication was unavailable and GDI was used (SDR only).</param>
/// <param name="Hdr">The raw HDR pixels, when requested and the screen was in HDR.</param>
internal sealed record CaptureResult(Bitmap Image, Rectangle Bounds, bool WasHdr, bool UsedFallback = false, HdrFrame? Hdr = null) : IDisposable
{
    public void Dispose() => Image.Dispose();
}

/// <summary>
/// Captures a whole monitor with DXGI Desktop Duplication, the same OS path OBS and Game Bar use,
/// so it sees fullscreen games without touching them. Falls back to GDI if duplication is unavailable.
/// </summary>
internal static class ScreenCapturer
{
    internal static readonly FeatureLevel[] FeatureLevels =
        [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0];

    internal static readonly Format[] SupportedFormats = [Format.R16G16B16A16_Float, Format.B8G8R8A8_UNorm];

    // Creating a Direct3D device takes ~200 ms, so keep one per graphics adapter. Captures never overlap,
    // but the lock keeps the shared device context safe regardless.
    private static readonly object DeviceGate = new();
    private static readonly Dictionary<long, (ID3D11Device Device, ID3D11DeviceContext Context)> Devices = [];

    /// <summary>Creates the graphics device ahead of time so the first screenshot is as fast as the rest.</summary>
    public static void WarmUp()
    {
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            if (factory.EnumAdapters1(0, out var adapter).Success)
                using (adapter) lock (DeviceGate) DeviceFor(adapter);
        }
        catch (Exception ex)
        {
            Log.Write($"Warm-up skipped: {ex.Message}");
        }
    }

    /// <param name="keepHdr">Also return the untouched HDR pixels (for saving an HDR copy).</param>
    public static CaptureResult CaptureMonitorAt(Point screenPoint, bool keepHdr = false)
    {
        var monitor = MonitorFromPoint(screenPoint, MonitorDefaultToNearest);
        try
        {
            lock (DeviceGate) return CaptureWithDuplication(monitor, keepHdr, retryOnLostDevice: true);
        }
        catch (Exception ex)
        {
            Log.Write($"Desktop Duplication failed, using GDI fallback: {ex}");
            return CaptureWithGdi(monitor);
        }
    }

    private static CaptureResult CaptureWithDuplication(IntPtr monitor, bool keepHdr, bool retryOnLostDevice)
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
        {
            using (adapter)
            {
                for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
                {
                    using (output)
                    {
                        if (output.Description.Monitor != monitor) continue;
                        try
                        {
                            return Duplicate(adapter, output, keepHdr);
                        }
                        catch (SharpGen.Runtime.SharpGenException ex) when (retryOnLostDevice && IsLostDevice(ex))
                        {
                            // Driver update, GPU reset or display mode change: start again with a fresh device.
                            ForgetDevice(adapter);
                            return CaptureWithDuplication(monitor, keepHdr, retryOnLostDevice: false);
                        }
                    }
                }
            }
        }
        throw new InvalidOperationException("The monitor was not found among the graphics adapter's outputs.");
    }

    private static CaptureResult Duplicate(IDXGIAdapter1 adapter, IDXGIOutput output, bool keepHdr)
    {
        using var output6 = output.QueryInterface<IDXGIOutput6>();
        var desc = output6.Description1;
        bool hdr = desc.ColorSpace == ColorSpaceType.RgbFullG2084NoneP2020;
        var bounds = Rectangle.FromLTRB(desc.DesktopCoordinates.Left, desc.DesktopCoordinates.Top,
            desc.DesktopCoordinates.Right, desc.DesktopCoordinates.Bottom);

        var (device, context) = DeviceFor(adapter);
        using (var duplication = output6.DuplicateOutput1(device, (uint)SupportedFormats.Length, SupportedFormats))
        {
            Bitmap? image = null;
            HdrFrame? hdrFrame = null;
            // The first frame should hold the whole desktop, but some drivers hand back an all-black first frame
            // and only send a real one once something on screen changes. Games redraw constantly; a still desktop
            // doesn't, so if the first frame is no good, briefly show an invisible 1-pixel window to make Windows
            // compose a fresh frame.
            var poke = IntPtr.Zero;
            try
            {
            for (int attempt = 0; attempt < 5 && image is null; attempt++)
            {
                if (attempt == 1) poke = ShowPokeWindow(bounds);
                var hr = duplication.AcquireNextFrame(attempt == 0 ? 500u : 150u, out var frame, out var resource);
                if (hr == Vortice.DXGI.ResultCode.WaitTimeout) continue;
                hr.CheckError();
                try
                {
                    // After the first frame, a zero present time means only the mouse moved: nothing new to read.
                    if (attempt > 0 && frame.LastPresentTime == 0) continue;
                    using (resource)
                    using (var texture = resource!.QueryInterface<ID3D11Texture2D>())
                        (image, hdrFrame) = ReadTexture(device, context, texture, desc.DeviceName, keepHdr);
                }
                finally
                {
                    duplication.ReleaseFrame();
                }
                if (LooksBlank(image))
                {
                    image.Dispose();
                    image = null;
                    hdrFrame = null;
                }
            }
            }
            finally
            {
                if (poke != IntPtr.Zero) DestroyWindow(poke);
            }

            if (image is null) throw new TimeoutException("No desktop frame arrived.");
            ApplyRotation(image, desc.Rotation);
            if (hdrFrame is not null && desc.Rotation is not (ModeRotation.Identity or ModeRotation.Unspecified))
            {
                Log.Write("HDR copy skipped: rotated monitors aren't supported for HDR copies yet");
                hdrFrame = null;
            }
            if (image.Width != bounds.Width || image.Height != bounds.Height)
            {
                image.Dispose();
                throw new InvalidOperationException($"Frame size did not match the monitor ({bounds.Size}).");
            }
            return new CaptureResult(image, bounds, hdr, Hdr: hdrFrame);
        }
    }

    private static (ID3D11Device Device, ID3D11DeviceContext Context) DeviceFor(IDXGIAdapter1 adapter)
    {
        long key = LuidKey(adapter);
        if (Devices.TryGetValue(key, out var cached)) return cached;
        D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport, FeatureLevels,
            out ID3D11Device? device, out ID3D11DeviceContext? context).CheckError();
        var created = (device!, context!);
        Devices[key] = created;
        return created;
    }

    private static void ForgetDevice(IDXGIAdapter1 adapter)
    {
        if (!Devices.Remove(LuidKey(adapter), out var old)) return;
        old.Context.Dispose();
        old.Device.Dispose();
    }

    private static long LuidKey(IDXGIAdapter1 adapter)
    {
        var luid = adapter.Description1.Luid;
        return ((long)luid.HighPart << 32) | luid.LowPart;
    }

    private static bool IsLostDevice(SharpGen.Runtime.SharpGenException ex) =>
        ex.ResultCode == Vortice.DXGI.ResultCode.DeviceRemoved ||
        ex.ResultCode == Vortice.DXGI.ResultCode.DeviceReset ||
        ex.ResultCode == Vortice.DXGI.ResultCode.AccessLost;

    private static (Bitmap Image, HdrFrame? Hdr) ReadTexture(ID3D11Device device, ID3D11DeviceContext context, ID3D11Texture2D texture,
        string deviceName, bool keepHdr)
    {
        var desc = texture.Description;
        var stagingDesc = desc with
        {
            MipLevels = 1,
            ArraySize = 1,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
        };
        using var staging = device.CreateTexture2D(stagingDesc);
        context.CopyResource(staging, texture);
        var mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            int width = (int)desc.Width, height = (int)desc.Height;
            return desc.Format switch
            {
                Format.B8G8R8A8_UNorm => (FromBgra8(mapped.DataPointer, (int)mapped.RowPitch, width, height), null),
                Format.R16G16B16A16_Float => (FromScRgb16(mapped.DataPointer, (int)mapped.RowPitch, width, height,
                    DisplayInfo.SdrWhiteScRgb(deviceName)), keepHdr ? CopyHdr(mapped.DataPointer, (int)mapped.RowPitch, width, height) : null),
                _ => throw new NotSupportedException($"Unexpected desktop format {desc.Format}."),
            };
        }
        finally
        {
            context.Unmap(staging, 0);
        }
    }

    internal static unsafe Bitmap FromBgra8(IntPtr src, int srcPitch, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            Parallel.For(0, height, y =>
            {
                var from = (uint*)((byte*)src + (long)y * srcPitch);
                var to = (uint*)((byte*)data.Scan0 + (long)y * data.Stride);
                // The desktop's alpha channel is meaningless; force opaque so pastes and PNGs aren't see-through.
                for (int x = 0; x < width; x++) to[x] = from[x] | 0xFF000000u;
            });
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }

    private static unsafe HdrFrame CopyHdr(IntPtr src, int srcPitch, int width, int height)
    {
        var pixels = new Half[width * height * 4];
        fixed (Half* dst = pixels)
        {
            var d = dst;
            Parallel.For(0, height, y =>
                Buffer.MemoryCopy((byte*)src + (long)y * srcPitch, d + (long)y * width * 4, (long)width * 8, (long)width * 8));
        }
        return new HdrFrame(width, height, pixels);
    }

    private static Bitmap FromScRgb16(IntPtr src, int srcPitch, int width, int height, float sdrWhite)
    {
        float highlightMax = HighlightMax(src, srcPitch, width, height, sdrWhite);
        var bitmap = ToneMap(src, srcPitch, width, height, new HdrToneMapper(sdrWhite, highlightMax));
        Log.Write($"HDR capture {width}x{height}, SDR white {sdrWhite * 80:0} nits, highlights up to {highlightMax:0.00}x white");
        return bitmap;
    }

    /// <summary>How far the highlights go, relative to SDR white, ignoring a few hot pixels.</summary>
    internal static unsafe float HighlightMax(IntPtr src, int srcPitch, int width, int height, float sdrWhite)
    {
        const int step = 4;
        var samples = new List<float>(width / step * (height / step) + 1);
        float invWhite = 1f / sdrWhite;
        for (int y = 0; y < height; y += step)
        {
            var row = (Half*)((byte*)src + (long)y * srcPitch);
            for (int x = 0; x < width; x += step)
            {
                var p = row + x * 4;
                samples.Add(HdrToneMapper.Luminance((float)p[0], (float)p[1], (float)p[2]) * invWhite);
            }
        }
        return MathF.Max(1f, HdrToneMapper.PercentileOf(samples.ToArray(), 0.995));
    }

    internal static unsafe Bitmap ToneMap(IntPtr src, int srcPitch, int width, int height, HdrToneMapper mapper)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            Parallel.For(0, height, y =>
            {
                var from = (Half*)((byte*)src + (long)y * srcPitch);
                var to = (byte*)data.Scan0 + (long)y * data.Stride;
                for (int x = 0; x < width; x++)
                {
                    var p = from + x * 4;
                    var c = mapper.Map((float)p[0], (float)p[1], (float)p[2]);
                    var o = to + x * 4;
                    o[0] = c.B;
                    o[1] = c.G;
                    o[2] = c.R;
                    o[3] = 255;
                }
            });
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }

    internal static unsafe bool LooksBlank(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int sy = 1; sy < 16; sy++)
            {
                var row = (uint*)((byte*)data.Scan0 + (long)(bitmap.Height * sy / 16) * data.Stride);
                for (int sx = 1; sx < 16; sx++)
                    if ((row[bitmap.Width * sx / 16] & 0x00FFFFFFu) != 0) return false;
            }
            return true;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static void ApplyRotation(Bitmap image, ModeRotation rotation)
    {
        // Duplication returns the panel's native orientation; turn it to match the desktop.
        switch (rotation)
        {
            case ModeRotation.Rotate90: image.RotateFlip(RotateFlipType.Rotate90FlipNone); break;
            case ModeRotation.Rotate180: image.RotateFlip(RotateFlipType.Rotate180FlipNone); break;
            case ModeRotation.Rotate270: image.RotateFlip(RotateFlipType.Rotate270FlipNone); break;
        }
    }

    internal static CaptureResult CaptureWithGdi(IntPtr monitor)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) throw new InvalidOperationException("Could not read monitor bounds.");
        var bounds = Rectangle.FromLTRB(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
        return new CaptureResult(bitmap, bounds, WasHdr: false, UsedFallback: true);
    }

    internal static IntPtr ShowPokeWindow(Rectangle bounds)
    {
        const uint wsPopup = 0x80000000;
        const uint exLayered = 0x80000, exTransparent = 0x20, exToolWindow = 0x80, exNoActivate = 0x08000000, exTopmost = 0x8;
        var hwnd = CreateWindowEx(exLayered | exTransparent | exToolWindow | exNoActivate | exTopmost, "STATIC", "", wsPopup,
            bounds.Right - 1, bounds.Bottom - 1, 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (hwnd == IntPtr.Zero) return IntPtr.Zero;
        // Alpha 1 of 255: changes at most one level of one corner pixel, so it never shows in the screenshot.
        SetLayeredWindowAttributes(hwnd, 0, 1, 0x2);
        ShowWindow(hwnd, 4 /* SW_SHOWNOACTIVATE */);
        return hwnd;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    internal static extern bool DestroyWindow(IntPtr hwnd);

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor; public NativeRect Work; public uint Flags; }

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromPoint(Point pt, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
