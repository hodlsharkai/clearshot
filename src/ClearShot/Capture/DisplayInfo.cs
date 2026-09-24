using System.Runtime.InteropServices;

namespace ClearShot.Capture;

/// <summary>Reads Windows' "SDR content brightness" for a monitor, which defines what white looks like in HDR mode.</summary>
internal static class DisplayInfo
{
    /// <summary>Used when Windows won't say: 200 nits, a typical SDR white on HDR desktops.</summary>
    public const float FallbackSdrWhiteScRgb = 2.5f;

    private const uint QdcOnlyActivePaths = 0x2;
    private const uint GetSourceName = 1;
    private const uint GetSdrWhiteLevel = 11;

    /// <summary>SDR white in scRGB units (1.0 = 80 nits) for the display whose GDI name is e.g. \\.\DISPLAY1.</summary>
    public static float SdrWhiteScRgb(string gdiDeviceName)
    {
        try
        {
            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out uint pathCount, out uint modeCount) != 0)
                return FallbackSdrWhiteScRgb;
            var paths = new DisplayConfigPathInfo[pathCount];
            var modes = new DisplayConfigModeInfo[modeCount];
            if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
                return FallbackSdrWhiteScRgb;

            for (int i = 0; i < pathCount; i++)
            {
                var source = new DisplayConfigSourceDeviceName
                {
                    Header = new DisplayConfigDeviceInfoHeader
                    {
                        Type = GetSourceName,
                        Size = (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
                        AdapterId = paths[i].SourceInfo.AdapterId,
                        Id = paths[i].SourceInfo.Id,
                    },
                };
                if (DisplayConfigGetDeviceInfo(ref source) != 0) continue;
                if (!string.Equals(source.ViewGdiDeviceName, gdiDeviceName, StringComparison.OrdinalIgnoreCase)) continue;

                var white = new DisplayConfigSdrWhiteLevel
                {
                    Header = new DisplayConfigDeviceInfoHeader
                    {
                        Type = GetSdrWhiteLevel,
                        Size = (uint)Marshal.SizeOf<DisplayConfigSdrWhiteLevel>(),
                        AdapterId = paths[i].TargetInfo.AdapterId,
                        Id = paths[i].TargetInfo.Id,
                    },
                };
                if (DisplayConfigGetDeviceInfo(ref white) != 0 || white.SdrWhiteLevel == 0) break;
                // SDRWhiteLevel is in thousandths of 80 nits, which is exactly scRGB units x 1000.
                return white.SdrWhiteLevel / 1000f;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Write($"DisplayConfig unavailable: {ex.Message}");
        }
        return FallbackSdrWhiteScRgb;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathSourceInfo { public Luid AdapterId; public uint Id; public uint ModeInfoIdx; public uint StatusFlags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigRational { public uint Numerator; public uint Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public DisplayConfigRational RefreshRate;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo { public DisplayConfigPathSourceInfo SourceInfo; public DisplayConfigPathTargetInfo TargetInfo; public uint Flags; }

    // Only the size matters here; the union contents are never read.
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct DisplayConfigModeInfo { public uint InfoType; public uint Id; public Luid AdapterId; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDeviceInfoHeader { public uint Type; public uint Size; public Luid AdapterId; public uint Id; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigSourceDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigSdrWhiteLevel { public DisplayConfigDeviceInfoHeader Header; public uint SdrWhiteLevel; }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DisplayConfigPathInfo[] pathArray,
        ref uint numModeInfoArrayElements, [Out] DisplayConfigModeInfo[] modeInfoArray, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName requestPacket);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSdrWhiteLevel requestPacket);
}
