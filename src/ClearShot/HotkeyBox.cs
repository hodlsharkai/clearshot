using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ClearShot;

/// <summary>Click it, press the shortcut you want, and it records it.</summary>
internal sealed class HotkeyBox : TextBox
{
    private Hotkey _value;

    /// <summary>Raised once a shortcut has been recorded, or Esc pressed, so the form can move focus on.</summary>
    public event EventHandler? Done;

    public HotkeyBox()
    {
        ReadOnly = true;
        BackColor = SystemColors.Window;
        Cursor = Cursors.Hand;
        ShortcutsEnabled = false;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Hotkey Value
    {
        get => _value;
        set
        {
            _value = value;
            Text = value.DisplayText;
        }
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        Text = "Press a shortcut…";
    }

    protected override void OnLeave(EventArgs e)
    {
        base.OnLeave(e);
        Text = _value.DisplayText;
    }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) != Keys.Tab || (keyData & Keys.Modifiers) != 0;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
        if (e.KeyCode == Keys.Escape && e.Modifiers == Keys.None)
        {
            Text = _value.DisplayText;
            Done?.Invoke(this, EventArgs.Empty);
            return;
        }
        TryAccept(e.KeyCode, e.Modifiers);
    }

    // Windows only sends Print Screen as a key-up, never a key-down.
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.PrintScreen) TryAccept(e.KeyCode, e.Modifiers);
        base.OnKeyUp(e);
    }

    private void TryAccept(Keys key, Keys modifiers)
    {
        if (!Hotkey.IsUsableKey(key)) return;
        bool win = (GetKeyState(0x5B) & 0x8000) != 0 || (GetKeyState(0x5C) & 0x8000) != 0;
        Value = new Hotkey(key, modifiers.HasFlag(Keys.Control), modifiers.HasFlag(Keys.Alt), modifiers.HasFlag(Keys.Shift), win);
        Done?.Invoke(this, EventArgs.Empty);
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);
}
