using System.Runtime.InteropServices;

namespace ClearShot;

/// <summary>Registers system-wide shortcuts on a hidden message-only window.</summary>
internal sealed class HotkeyManager : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private static readonly IntPtr MessageOnlyParent = new(-3);
    private readonly HashSet<int> _registered = [];

    public event Action<int>? Pressed;

    public HotkeyManager() => CreateHandle(new CreateParams { Parent = MessageOnlyParent });

    /// <returns>False if another app (or Windows itself) already owns the shortcut.</returns>
    public bool Register(int id, Hotkey hotkey)
    {
        Unregister(id);
        if (!RegisterHotKey(Handle, id, hotkey.NativeModifiers, (uint)hotkey.Key)) return false;
        _registered.Add(id);
        return true;
    }

    public void Unregister(int id)
    {
        if (_registered.Remove(id)) UnregisterHotKey(Handle, id);
    }

    public void UnregisterAll()
    {
        foreach (var id in _registered.ToArray()) Unregister(id);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey) Pressed?.Invoke(m.WParam.ToInt32());
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
