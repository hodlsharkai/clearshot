using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace ClearShot.Capture;

/// <summary>One frame of a recording: BGRA pixels at the output size, shown for <see cref="DurationMs"/>.</summary>
internal sealed class RecordedFrame(byte[] bgra, int durationMs)
{
    public byte[] Bgra { get; } = bgra;
    public int DurationMs { get; set; } = durationMs;
}

/// <param name="Width">Output width (the area shrunk to fit the size limit).</param>
internal sealed record Recording(IReadOnlyList<RecordedFrame> Frames, int Width, int Height)
{
    public int TotalMs => Frames.Sum(f => f.DurationMs);
}

/// <summary>
/// Records an area of one monitor at a steady frame rate using Desktop Duplication. Only the chosen area is
/// copied off the graphics card each frame, so it stays smooth on 4K and HDR screens. Frames that didn't
/// change are merged into the previous one, which keeps GIFs of mostly-still content small.
/// </summary>
internal sealed class RegionRecorder(IntPtr monitor, Rectangle area, int fps, int maxWidth, TimeSpan maxDuration)
{
    public Task<Recording> RunAsync(CancellationToken stop) =>
        Task.Factory.StartNew(() => Run(stop), stop, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private Recording Run(CancellationToken stop)
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
                        using var output6 = output.QueryInterface<IDXGIOutput6>();
                        return Record(adapter, output6, stop);
                    }
                }
            }
        }
        throw new InvalidOperationException("The monitor was not found among the graphics adapter's outputs.");
    }

    private Recording Record(IDXGIAdapter1 adapter, IDXGIOutput6 output, CancellationToken stop)
    {
        var desc = output.Description1;
        var monitorBounds = Rectangle.FromLTRB(desc.DesktopCoordinates.Left, desc.DesktopCoordinates.Top,
            desc.DesktopCoordinates.Right, desc.DesktopCoordinates.Bottom);
        if (desc.Rotation is not (ModeRotation.Identity or ModeRotation.Unspecified))
            throw new NotSupportedException("GIF recording on a rotated monitor isn't supported yet.");
        var region = Rectangle.Intersect(area, new Rectangle(Point.Empty, monitorBounds.Size));
        if (region.Width < 2 || region.Height < 2) throw new ArgumentException("The recording area is too small.");

        float scale = Math.Min(1f, (float)maxWidth / region.Width);
        int outW = Math.Max(1, (int)Math.Round(region.Width * scale));
        int outH = Math.Max(1, (int)Math.Round(region.Height * scale));

        D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport, ScreenCapturer.FeatureLevels,
            out ID3D11Device? device, out ID3D11DeviceContext? context).CheckError();
        using (device)
        using (context)
        using (var duplication = output.DuplicateOutput1(device!, (uint)ScreenCapturer.SupportedFormats.Length, ScreenCapturer.SupportedFormats))
        {
            var frames = new List<RecordedFrame>();
            ID3D11Texture2D? staging = null;
            HdrToneMapper? toneMapper = null;
            byte[]? last = null;
            var poke = IntPtr.Zero;
            var clock = Stopwatch.StartNew();
            double frameMs = 1000.0 / fps;
            long frameIndex = 0;
            try
            {
                while (!stop.IsCancellationRequested && clock.Elapsed < maxDuration)
                {
                    // Wait for this frame's moment, then take whatever the screen shows right now.
                    double dueMs = frameIndex * frameMs;
                    int waitMs = (int)Math.Max(0, dueMs - clock.Elapsed.TotalMilliseconds);
                    if (waitMs > 0 && stop.WaitHandle.WaitOne(waitMs)) break;

                    byte[]? pixels = null;
                    var hr = duplication.AcquireNextFrame(last is null ? 250u : 0u, out var info, out var resource);
                    if (hr.Success)
                    {
                        try
                        {
                            // Before the first real image, skip frames with nothing new (some drivers send black first).
                            if (last is not null || info.LastPresentTime != 0)
                            {
                                using (resource)
                                using (var texture = resource!.QueryInterface<ID3D11Texture2D>())
                                {
                                    var full = texture.Description;
                                    staging ??= device!.CreateTexture2D(new Texture2DDescription
                                    {
                                        Width = (uint)region.Width,
                                        Height = (uint)region.Height,
                                        MipLevels = 1,
                                        ArraySize = 1,
                                        Format = full.Format,
                                        SampleDescription = new SampleDescription(1, 0),
                                        Usage = ResourceUsage.Staging,
                                        CPUAccessFlags = CpuAccessFlags.Read,
                                    });
                                    var box = new Vortice.Mathematics.Box(region.Left, region.Top, 0, region.Right, region.Bottom, 1);
                                    context!.CopySubresourceRegion(staging, 0, 0, 0, 0, texture, 0, box);
                                    pixels = ReadScaled(context, staging, full.Format, region.Size, outW, outH, desc.DeviceName, ref toneMapper);
                                }
                            }
                        }
                        finally
                        {
                            duplication.ReleaseFrame();
                        }
                    }
                    else if (hr != Vortice.DXGI.ResultCode.WaitTimeout)
                    {
                        hr.CheckError();
                    }

                    if (pixels is null && last is null)
                    {
                        // Still no first image: prompt Windows to compose a fresh frame, as for screenshots.
                        if (poke == IntPtr.Zero) poke = ScreenCapturer.ShowPokeWindow(monitorBounds);
                        continue;
                    }

                    frameIndex++;
                    if (pixels is null || (last is not null && pixels.AsSpan().SequenceEqual(last)))
                    {
                        frames[^1].DurationMs = (int)Math.Round(frameIndex * frameMs) - frames.Sum(f => f.DurationMs) + frames[^1].DurationMs;
                        continue;
                    }
                    int start = frames.Sum(f => f.DurationMs);
                    frames.Add(new RecordedFrame(pixels, (int)Math.Round(frameIndex * frameMs) - start));
                    last = pixels;
                    if (poke != IntPtr.Zero) { ScreenCapturer.DestroyWindow(poke); poke = IntPtr.Zero; }
                }
            }
            finally
            {
                if (poke != IntPtr.Zero) ScreenCapturer.DestroyWindow(poke);
                staging?.Dispose();
            }

            if (frames.Count == 0) throw new InvalidOperationException("No frames were recorded.");
            Log.Write($"Recorded {frames.Count} distinct frames, {outW}x{outH}, {frames.Sum(f => f.DurationMs)} ms, HDR={toneMapper is not null}");
            return new Recording(frames, outW, outH);
        }
    }

    private static byte[] ReadScaled(ID3D11DeviceContext context, ID3D11Texture2D staging, Format format, Size size,
        int outW, int outH, string deviceName, ref HdrToneMapper? toneMapper)
    {
        var mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        Bitmap native;
        try
        {
            if (format == Format.R16G16B16A16_Float)
            {
                if (toneMapper is null)
                {
                    // Fix the brightness from the first frame so it doesn't flicker frame to frame.
                    float white = DisplayInfo.SdrWhiteScRgb(deviceName);
                    float highlights = ScreenCapturer.HighlightMax(mapped.DataPointer, (int)mapped.RowPitch, size.Width, size.Height, white);
                    toneMapper = new HdrToneMapper(white, Math.Max(highlights, 1.5f));
                }
                native = ScreenCapturer.ToneMap(mapped.DataPointer, (int)mapped.RowPitch, size.Width, size.Height, toneMapper);
            }
            else if (format == Format.B8G8R8A8_UNorm)
            {
                native = ScreenCapturer.FromBgra8(mapped.DataPointer, (int)mapped.RowPitch, size.Width, size.Height);
            }
            else throw new NotSupportedException($"Unexpected desktop format {format}.");
        }
        finally
        {
            context.Unmap(staging, 0);
        }

        using (native)
        {
            Bitmap output = native;
            Bitmap? scaled = null;
            if (outW != size.Width || outH != size.Height)
            {
                scaled = new Bitmap(outW, outH, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(scaled);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(native, new Rectangle(0, 0, outW, outH));
                output = scaled;
            }
            using (scaled)
            {
                var data = output.LockBits(new Rectangle(0, 0, outW, outH), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var bytes = new byte[outW * outH * 4];
                    for (int y = 0; y < outH; y++)
                        System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * outW * 4, outW * 4);
                    return bytes;
                }
                finally
                {
                    output.UnlockBits(data);
                }
            }
        }
    }
}
