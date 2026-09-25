using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ClearShot;

/// <summary>
/// Watches PlayStation controllers (DualShock 4, DualSense, DualSense Edge; USB or Bluetooth) for a press of the touchpad
/// or the Share/Create button, read straight from the controller's own reports: XInput doesn't know the touchpad exists,
/// and PlayStation pads only reach XInput through DS4Windows or Steam. If DS4Windows hides the controller from other apps
/// (its HidHide option), it can't be seen here and this quietly finds nothing.
/// </summary>
internal sealed class SonyTouchpad : IDisposable
{
    internal enum Button { Touchpad, Share }

    private readonly Button _button;
    private const ushort Sony = 0x054C;
    private static readonly HashSet<ushort> DualShock4 = [0x05C4, 0x09CC, 0x0BA0];
    private static readonly HashSet<ushort> DualSense = [0x0CE6, 0x0DF2];

    private readonly Action _clicked;
    private bool _toldBlocked;

    /// <summary>
    /// Raised once if a PlayStation controller is there but Windows won't let ClearShot read it: DS4Windows' HidHide is
    /// hiding it. Allowing ClearShot in the HidHide Configuration Client (Applications) fixes it.
    /// </summary>
    public event Action? Blocked;
    private readonly CancellationTokenSource _stop = new();
    private readonly HashSet<string> _open = [];
    private readonly object _gate = new();

    public SonyTouchpad(Button button, Action clicked)
    {
        _button = button;
        _clicked = clicked;
        Task.Run(FindControllers);
    }

    /// <summary>Is the button pressed in this input report? Null if the report isn't one that carries it.</summary>
    internal static bool? ButtonDown(bool dualSense, ReadOnlySpan<byte> report, Button button)
    {
        if (report.Length < 11) return null;
        int at = (dualSense, report[0]) switch
        {
            (false, 0x01) => 7,  // DualShock 4, USB (and Bluetooth before full reports start): PS + touchpad in byte 7
            (false, 0x11) => 9,  // DualShock 4, Bluetooth: 2 extra bytes in front
            (true, 0x01) when report.Length >= 64 => 10, // DualSense, USB
            (true, 0x01) => 7,   // DualSense, Bluetooth simple report
            (true, 0x31) => 11,  // DualSense, Bluetooth full report
            _ => -1,
        };
        if (at < 0 || at >= report.Length) return null;
        // Touchpad click is bit 1 of that byte; Share/Create is bit 4 of the byte before (next to L1, R1, Options...).
        return button == Button.Touchpad ? (report[at] & 0x02) != 0 : (report[at - 1] & 0x10) != 0;
    }

    private async Task FindControllers()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                foreach (var path in HidPaths())
                {
                    lock (_gate) { if (_open.Contains(path)) continue; }
                    var found = Identify(path, out bool denied);
                    if (denied && !_toldBlocked)
                    {
                        _toldBlocked = true;
                        Log.Write("Touchpad: a PlayStation controller is hidden from ClearShot (access denied): HidHide from DS4Windows");
                        Blocked?.Invoke();
                    }
                    if (found is not (bool dualSense, int reportLength)) continue;
                    lock (_gate) _open.Add(path);
                    _ = Task.Run(() => Listen(path, dualSense, reportLength));
                }
            }
            catch (Exception ex)
            {
                Log.Write($"Touchpad: looking for controllers failed: {ex.Message}");
            }
            try { await Task.Delay(3000, _stop.Token); } catch (OperationCanceledException) { }
        }
    }

    /// <returns>Whether it's a DualSense (else DualShock 4) and its input report length, or null if it isn't a PlayStation pad.</returns>
    private static (bool, int)? Identify(string path, out bool denied)
    {
        using var handle = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        denied = handle.IsInvalid && Marshal.GetLastWin32Error() == 5 && IsSonyPath(path);
        if (handle.IsInvalid) return null;
        var attributes = new HiddAttributes { Size = Marshal.SizeOf<HiddAttributes>() };
        if (!HidD_GetAttributes(handle, ref attributes) || attributes.VendorId != Sony) return null;
        bool dualSense = DualSense.Contains(attributes.ProductId);
        if (!dualSense && !DualShock4.Contains(attributes.ProductId)) return null;
        int length = 64;
        if (HidD_GetPreparsedData(handle, out var data))
        {
            if (HidP_GetCaps(data, out var caps) == 0x110000) length = Math.Max(length, caps.InputReportByteLength);
            HidD_FreePreparsedData(data);
        }
        return (dualSense, length);
    }

    private async Task Listen(string path, bool dualSense, int reportLength)
    {
        try
        {
            using var handle = CreateFile(path, 0x80000000 /* GENERIC_READ */, 3, IntPtr.Zero, 3, 0x40000000 /* OVERLAPPED */, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                Log.Write($"Touchpad: can't read a PlayStation controller (error {Marshal.GetLastWin32Error()}); DS4Windows may be hiding it");
                return;
            }
            Log.Write($"Touchpad: watching a {(dualSense ? "DualSense" : "DualShock 4")}");
            await using var stream = new FileStream(handle, FileAccess.Read, 0, isAsync: true);
            var buffer = new byte[Math.Max(reportLength, 64)];
            bool wasDown = false;
            while (!_stop.IsCancellationRequested)
            {
                int n = await stream.ReadAsync(buffer, _stop.Token);
                if (n <= 0) break;
                if (ButtonDown(dualSense, buffer.AsSpan(0, n), _button) is not bool down) continue;
                if (down && !wasDown)
                {
                    try { _clicked(); }
                    catch (Exception ex) { Log.Write($"Touchpad shortcut failed: {ex.Message}"); }
                }
                wasDown = down;
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { /* unplugged or switched off: found again when it comes back */ }
        catch (Exception ex)
        {
            Log.Write($"Touchpad: stopped reading a controller: {ex.Message}");
        }
        finally
        {
            lock (_gate) _open.Remove(path);
        }
    }

    /// <summary>Sony's vendor id and a controller's product id in a device path (USB "vid_054c&amp;pid_0ce6", Bluetooth "vid&amp;0002054c_pid&amp;0ce6").</summary>
    internal static bool IsSonyPath(string path)
    {
        var p = path.ToLowerInvariant();
        return p.Contains("054c") && DualShock4.Concat(DualSense).Any(id => p.Contains($"pid&{id:x4}") || p.Contains($"pid_{id:x4}"));
    }

    private static IEnumerable<string> HidPaths()
    {
        HidD_GetHidGuid(out var hid);
        var set = SetupDiGetClassDevs(ref hid, IntPtr.Zero, IntPtr.Zero, 0x12 /* PRESENT | DEVICEINTERFACE */);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) yield break;
        try
        {
            var item = new DeviceInterfaceData { Size = Marshal.SizeOf<DeviceInterfaceData>() };
            for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hid, i, ref item); i++)
            {
                SetupDiGetDeviceInterfaceDetail(set, ref item, IntPtr.Zero, 0, out uint size, IntPtr.Zero);
                if (size == 0) continue;
                var detail = Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6); // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref item, detail, size, out _, IntPtr.Zero)) continue;
                    var path = Marshal.PtrToStringUni(detail + 4);
                    // Cheap filter before opening anything: Sony's vendor id appears in the path (USB and Bluetooth).
                    if (path is not null && path.Contains("054c", StringComparison.OrdinalIgnoreCase)) yield return path;
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
    }

    [StructLayout(LayoutKind.Sequential)] private struct HiddAttributes { public int Size; public ushort VendorId, ProductId, Version; }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInterfaceData { public int Size; public Guid Class; public int Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    private struct HidpCaps
    {
        public short Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public short[] Reserved;
        public short NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
            NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices, NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(SafeFileHandle device, ref HiddAttributes attributes);
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr data);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out HidpCaps caps);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, uint index, ref DeviceInterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref DeviceInterfaceData data, IntPtr detail, uint size, out uint required, IntPtr info);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
}
