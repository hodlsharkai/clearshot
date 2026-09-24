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

    // Keys nobody types in normal use, so they're fine as a shortcut on their own.
    private static readonly HashSet<Keys> StandaloneKeys =
    [
        Keys.PrintScreen, Keys.Pause, Keys.Scroll, Keys.Insert,
        Keys.F1, Keys.F2, Keys.F3, Keys.F4, Keys.F5, Keys.F6, Keys.F7, Keys.F8, Keys.F9, Keys.F10, Keys.F11, Keys.F12,
        Keys.F13, Keys.F14, Keys.F15, Keys.F16, Keys.F17, Keys.F18, Keys.F19, Keys.F20, Keys.F21, Keys.F22, Keys.F23, Keys.F24,
    ];

    /// <summary>
    /// False for shortcuts that would hijack ordinary typing, such as C or Shift + C:
    /// a global shortcut fires everywhere, so letters, numbers and the like need Ctrl, Alt or Win.
    /// </summary>
    public bool IsSafeAsGlobalShortcut => Ctrl || Alt || Win || StandaloneKeys.Contains(Key);

    public static bool IsUsableKey(Keys key) => !ModifierOnlyKeys.Contains(key) && Enum.IsDefined(key);

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
