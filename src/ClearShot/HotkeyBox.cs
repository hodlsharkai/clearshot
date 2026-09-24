using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ClearShot;

/// <summary>
/// Click it, then press the shortcut. Works whether the keys are held together (hold Alt + Shift, press C)
/// or tapped one after another (Alt, Shift, C): modifiers tapped while recording are remembered until a
/// real key arrives.
/// </summary>
internal sealed class HotkeyBox : TextBox
{
    private const int WmSysKeyUp = 0x105, VkMenu = 0x12, VkF10 = 0x79;
    private const string Prompt = "Press a shortcut, e.g. Alt + C";

    private Hotkey _value;
    private bool _tappedCtrl, _tappedAlt, _tappedShift, _tappedWin;

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
        ClearTapped();
        Text = Prompt;
    }

    protected override void OnLeave(EventArgs e)
    {
        base.OnLeave(e);
        ClearTapped();
        Text = _value.DisplayText;
    }

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) != Keys.Tab || (keyData & Keys.Modifiers) != 0;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
        HandleKeyDown(e.KeyCode, e.Modifiers, WinKeyHeld());
    }

    // Windows only sends Print Screen as a key-up, never a key-down.
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.PrintScreen) HandleKeyDown(e.KeyCode, e.Modifiers, WinKeyHeld());
        e.Handled = true;
        base.OnKeyUp(e);
    }

    protected override void WndProc(ref Message m)
    {
        // Letting go of Alt (or F10) on its own normally jumps to the window menu and swallows the next key.
        // While recording, Alt is part of the shortcut, so keep that from happening.
        if (m.Msg == WmSysKeyUp && Focused && (m.WParam.ToInt32() == VkMenu || m.WParam.ToInt32() == VkF10))
        {
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }

    /// <summary>The recording logic, separate from Windows messages so it can be tested.</summary>
    internal void HandleKeyDown(Keys key, Keys heldModifiers, bool winHeld)
    {
        if (key == Keys.Escape && heldModifiers == Keys.None && !AnyTapped)
        {
            ClearTapped();
            Text = _value.DisplayText;
            Done?.Invoke(this, EventArgs.Empty);
            return;
        }

        switch (key)
        {
            case Keys.ControlKey or Keys.LControlKey or Keys.RControlKey: _tappedCtrl = true; ShowProgress(); return;
            case Keys.Menu or Keys.LMenu or Keys.RMenu: _tappedAlt = true; ShowProgress(); return;
            case Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey: _tappedShift = true; ShowProgress(); return;
            case Keys.LWin or Keys.RWin: _tappedWin = true; ShowProgress(); return;
        }
        if (!Hotkey.IsUsableKey(key)) return;

        var candidate = new Hotkey(key,
            heldModifiers.HasFlag(Keys.Control) || _tappedCtrl,
            heldModifiers.HasFlag(Keys.Alt) || _tappedAlt,
            heldModifiers.HasFlag(Keys.Shift) || _tappedShift,
            winHeld || _tappedWin);
        ClearTapped();

        if (!candidate.IsSafeAsGlobalShortcut)
        {
            Text = $"{candidate.DisplayText} would fire while you type. Add Ctrl, Alt or Win.";
            return;
        }
        Value = candidate;
        Done?.Invoke(this, EventArgs.Empty);
    }

    private bool AnyTapped => _tappedCtrl || _tappedAlt || _tappedShift || _tappedWin;

    private void ShowProgress()
    {
        var parts = new List<string>(4);
        if (_tappedCtrl) parts.Add("Ctrl");
        if (_tappedAlt) parts.Add("Alt");
        if (_tappedShift) parts.Add("Shift");
        if (_tappedWin) parts.Add("Win");
        Text = string.Join(" + ", parts) + " + …";
    }

    private void ClearTapped() => _tappedCtrl = _tappedAlt = _tappedShift = _tappedWin = false;

    private static bool WinKeyHeld() => (GetKeyState(0x5B) & 0x8000) != 0 || (GetKeyState(0x5C) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);
}
