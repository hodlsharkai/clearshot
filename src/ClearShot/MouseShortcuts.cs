using System.Runtime.InteropServices;

namespace ClearShot;

/// <summary>
/// Shortcuts on mouse buttons (middle, back, forward), alone or with Ctrl/Alt/Shift/Win. Windows' own shortcut system
/// only handles keys, so a low-level mouse hook watches for them. A click that fires a shortcut is used up, so the app
/// under the mouse doesn't also act on it (mouse 4 doesn't also send the browser back).
/// </summary>
internal sealed class MouseShortcuts : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmMButtonDown = 0x207, WmMButtonUp = 0x208, WmXButtonDown = 0x20B, WmXButtonUp = 0x20C;
    private const int VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12, VkLWin = 0x5B, VkRWin = 0x5C;

    private readonly HookProc _proc;
    private readonly Dictionary<int, Hotkey> _shortcuts = [];
    private readonly HashSet<Keys> _swallowUp = [];
    private IntPtr _hook;

    /// <summary>Raised with the shortcut's id when one is clicked.</summary>
    public event Action<int>? Pressed;

    public MouseShortcuts() => _proc = Hook;

    public static bool IsMouseButton(Keys key) => key is Keys.MButton or Keys.XButton1 or Keys.XButton2;

    public bool Register(int id, Hotkey hotkey)
    {
        if (!IsMouseButton(hotkey.Key)) return false;
        _shortcuts[id] = hotkey;
        if (_hook == IntPtr.Zero)
        {
            _hook = SetWindowsHookEx(WhMouseLl, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero) Log.Write($"Mouse shortcuts unavailable (error {Marshal.GetLastWin32Error()})");
        }
        return _hook != IntPtr.Zero;
    }

    public void Unregister(int id)
    {
        if (_shortcuts.Remove(id) && _shortcuts.Count == 0) Unhook();
    }

    public void UnregisterAll()
    {
        _shortcuts.Clear();
        _swallowUp.Clear();
        Unhook();
    }

    /// <summary>Which mouse button a hook message is about, and whether it went down.</summary>
    internal static (Keys Button, bool Down)? ButtonOf(int message, uint mouseData) => message switch
    {
        WmMButtonDown => (Keys.MButton, true),
        WmMButtonUp => (Keys.MButton, false),
        WmXButtonDown or WmXButtonUp => ((mouseData >> 16) == 1 ? Keys.XButton1 : Keys.XButton2, message == WmXButtonDown),
        _ => null,
    };

    /// <summary>The shortcut matching this button with exactly these modifiers held, if any.</summary>
    internal static int? Match(IReadOnlyDictionary<int, Hotkey> shortcuts, Keys button, bool ctrl, bool alt, bool shift, bool win)
    {
        foreach (var (id, hk) in shortcuts)
            if (hk.Key == button && hk.Ctrl == ctrl && hk.Alt == alt && hk.Shift == shift && hk.Win == win) return id;
        return null;
    }

    private IntPtr Hook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<MouseInfo>(data);
            if (ButtonOf((int)message, info.MouseData) is (Keys button, bool down))
            {
                if (!down && _swallowUp.Remove(button)) return 1; // the release of a click we used
                if (down && Match(_shortcuts, button, Held(VkControl), Held(VkMenu), Held(VkShift), Held(VkLWin) || Held(VkRWin)) is int id)
                {
                    _swallowUp.Add(button);
                    try { Pressed?.Invoke(id); }
                    catch (Exception ex) { Log.Write($"Mouse shortcut failed: {ex.Message}"); }
                    return 1;
                }
            }
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    private static bool Held(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private void Unhook()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    public void Dispose() => Unhook();

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInfo { public int X, Y; public uint MouseData, Flags, Time; public IntPtr Extra; }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
}
