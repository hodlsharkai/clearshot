using System.Runtime.InteropServices;
using System.Text;

namespace ClearShot.Editor;

/// <summary>
/// While the editor is open, every key goes to it, whichever window Windows thinks has focus. Windows often won't hand
/// the keyboard to an app that isn't in front (a game, a browser), and then typing went to the app behind while the
/// editor sat waiting. A low-level keyboard hook sees keys first; the ones the editor uses are handled and kept from
/// the app behind. Alt and Windows-key combinations (Alt+Tab and so on) pass through untouched.
/// </summary>
internal sealed class KeyboardGrab : IDisposable
{
    private const int WhKeyboardLl = 13, WmKeyDown = 0x100, WmSysKeyDown = 0x104;
    private const int VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12, VkCapital = 0x14, VkLWin = 0x5B, VkRWin = 0x5C;
    private readonly HookProc _proc; // kept alive: Windows calls it for as long as the hook exists
    private readonly Func<Keys, bool> _key;
    private readonly Action<char> _type;
    private IntPtr _hook;

    /// <summary>While true (a colour or font dialog, or the emoji search box, needs typing) keys go where Windows sends them.</summary>
    public bool Paused { get; set; }

    public bool Active => _hook != IntPtr.Zero;

    /// <param name="key">The editor's key handler; returns true if it used the key.</param>
    /// <param name="type">Called with each character typed when the key itself wasn't used (text being typed).</param>
    public KeyboardGrab(Func<Keys, bool> key, Action<char> type)
    {
        _key = key;
        _type = type;
        _proc = Hook;
        _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) Log.Write($"Keyboard grab unavailable (error {Marshal.GetLastWin32Error()}); keys go to the focused window");
    }

    private IntPtr Hook(int code, IntPtr message, IntPtr data)
    {
        if (code < 0 || Paused) return CallNextHookEx(_hook, code, message, data);
        var info = Marshal.PtrToStructure<KeyInfo>(data);
        int vk = (int)info.VkCode;
        bool alt = Down(VkMenu) || (info.Flags & 0x20) != 0;
        bool win = Down(VkLWin) || Down(VkRWin) || vk is VkLWin or VkRWin;
        // Leave Alt and Windows-key combinations to Windows, and the modifier keys themselves.
        if (alt || win || vk is VkShift or VkControl or VkMenu or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or VkCapital)
            return CallNextHookEx(_hook, code, message, data);

        if ((int)message is WmKeyDown or WmSysKeyDown)
        {
            var keys = (Keys)vk;
            if (Down(VkControl)) keys |= Keys.Control;
            if (Down(VkShift)) keys |= Keys.Shift;
            try
            {
                if (!_key(keys) && !Down(VkControl))
                    foreach (char c in Characters(info.VkCode, info.ScanCode)) _type(c);
            }
            catch (Exception ex)
            {
                Log.Write($"Editor key handling failed: {ex}");
            }
        }
        return 1; // handled: the app behind doesn't see it
    }

    private static bool Down(int vk) => (GetKeyState(vk) & 0x8000) != 0;

    /// <summary>What the key types on this keyboard layout, with Shift and Caps Lock as they are now.</summary>
    internal static string Characters(uint vk, uint scan)
    {
        var state = new byte[256];
        if (Down(VkShift)) state[VkShift] = 0x80;
        if ((GetKeyState(VkCapital) & 1) != 0) state[VkCapital] = 1;
        var text = new StringBuilder(8);
        // Flag 4: don't change the keyboard's dead-key state (so é and friends still work in other apps).
        int n = ToUnicodeEx(vk, scan, state, text, text.Capacity, 4, GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero)));
        return n > 0 ? text.ToString(0, n) : "";
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyInfo { public uint VkCode, ScanCode, Flags, Time; public IntPtr Extra; }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern short GetKeyState(int vk);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ToUnicodeEx(uint vk, uint scan, byte[] state, StringBuilder text, int size, uint flags, IntPtr layout);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr process);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
