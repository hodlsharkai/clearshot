namespace ClearShot;

/// <summary>A global shortcut such as Ctrl+PrintScreen. Stored as text in settings.</summary>
internal readonly record struct Hotkey(Keys Key, bool Ctrl, bool Alt, bool Shift, bool Win)
{
    private const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8, ModNoRepeat = 0x4000;

    // Keys has several values with two names; always write the same one.
    private static readonly Dictionary<Keys, string> CanonicalNames = new()
    {
        [Keys.PrintScreen] = "PrintScreen",
        [Keys.Enter] = "Enter",
        [Keys.PageUp] = "PageUp",
        [Keys.PageDown] = "PageDown",
        [Keys.CapsLock] = "CapsLock",
    };

    private static readonly Dictionary<Keys, string> FriendlyNames = new()
    {
        [Keys.PrintScreen] = "Print Screen",
        [Keys.PageUp] = "Page Up",
        [Keys.PageDown] = "Page Down",
        [Keys.CapsLock] = "Caps Lock",
        [Keys.Scroll] = "Scroll Lock",
        [Keys.Pause] = "Pause",
        [Keys.Insert] = "Insert",
        [Keys.Delete] = "Delete",
        [Keys.MButton] = "Middle mouse button",
        [Keys.XButton1] = "Mouse button 4 (back)",
        [Keys.XButton2] = "Mouse button 5 (forward)",
    };

    private static readonly HashSet<Keys> ModifierOnlyKeys =
    [
        Keys.ControlKey, Keys.LControlKey, Keys.RControlKey,
        Keys.ShiftKey, Keys.LShiftKey, Keys.RShiftKey,
        Keys.Menu, Keys.LMenu, Keys.RMenu,
        Keys.LWin, Keys.RWin, Keys.None,
        Keys.Control, Keys.Shift, Keys.Alt, Keys.Modifiers,
    ];

    public uint NativeModifiers =>
        (Ctrl ? ModControl : 0) | (Alt ? ModAlt : 0) | (Shift ? ModShift : 0) | (Win ? ModWin : 0) | ModNoRepeat;

    /// <summary>
    /// Any key can be a shortcut, on its own or with Ctrl/Alt/Shift/Win (it's the user's choice: a lone letter then fires
    /// everywhere). Mouse buttons too, except left and right click, which would make clicking anything impossible.
    /// </summary>
    public static bool IsUsableKey(Keys key) => !ModifierOnlyKeys.Contains(key) && key is not (Keys.LButton or Keys.RButton) && Enum.IsDefined(key);

    public bool IsMouse => MouseShortcuts.IsMouseButton(Key);

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        bool ctrl = false, alt = false, shift = false, win = false;
        Keys? key = null;
        foreach (var raw in text.Split('+'))
        {
            var token = raw.Trim();
            switch (token.ToLowerInvariant())
            {
                case "ctrl" or "control": ctrl = true; continue;
                case "alt": alt = true; continue;
                case "shift": shift = true; continue;
                case "win" or "windows": win = true; continue;
            }
            // Exactly one real key; Enum.TryParse would also accept numbers and comma lists, so rule those out.
            if (key is not null || token.Length == 0 || char.IsDigit(token[0]) || token.Contains(',')) return false;
            if (!Enum.TryParse<Keys>(token, ignoreCase: true, out var parsed) || !IsUsableKey(parsed)) return false;
            key = parsed;
        }

        if (key is null) return false;
        hotkey = new Hotkey(key.Value, ctrl, alt, shift, win);
        return true;
    }

    public override string ToString() => Join("+", CanonicalName(Key));

    public string DisplayText => Join(" + ", FriendlyName(Key));

    private string Join(string separator, string keyName)
    {
        var parts = new List<string>(5);
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(keyName);
        return string.Join(separator, parts);
    }

    private static string CanonicalName(Keys key) => CanonicalNames.TryGetValue(key, out var n) ? n : key.ToString();

    private static string FriendlyName(Keys key)
    {
        if (FriendlyNames.TryGetValue(key, out var n)) return n;
        if (key is >= Keys.D0 and <= Keys.D9) return ((char)('0' + (key - Keys.D0))).ToString();
        return CanonicalName(key);
    }
}
