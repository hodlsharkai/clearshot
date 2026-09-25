using System.Runtime.InteropServices;

namespace ClearShot;

/// <summary>
/// Takes a screenshot from a controller: hold one button and press another (View + RB, say). Reads Xbox controllers,
/// and anything that presents itself as one (PlayStation pads through DS4Windows or Steam, most PC pads), through
/// XInput, which works whichever app is in front. The Xbox Share button can't be used: Windows keeps it for Game Bar.
/// </summary>
internal sealed class ControllerShortcut : IDisposable
{
    [Flags]
    internal enum Buttons : ushort
    {
        None = 0,
        DpadUp = 0x0001, DpadDown = 0x0002, DpadLeft = 0x0004, DpadRight = 0x0008,
        Menu = 0x0010, View = 0x0020, LeftStick = 0x0040, RightStick = 0x0080,
        LB = 0x0100, RB = 0x0200, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000,
    }

    /// <summary>The combinations offered in Settings: value stored, label shown, buttons to hold together.</summary>
    public static readonly (string Value, string Label, Buttons Combo)[] Choices =
    [
        ("Off", "Off", Buttons.None),
        ("View+RB", "View + RB (Share + R1 on PlayStation)", Buttons.View | Buttons.RB),
        ("View+LB", "View + LB (Share + L1 on PlayStation)", Buttons.View | Buttons.LB),
        ("View+Menu", "View + Menu (Share + Options on PlayStation)", Buttons.View | Buttons.Menu),
        ("Sticks", "Both stick clicks (L3 + R3)", Buttons.LeftStick | Buttons.RightStick),
    ];

    public static Buttons ComboFor(string? value) => Choices.FirstOrDefault(c => c.Value == value, Choices[0]).Combo;

    private readonly Buttons _combo;
    private readonly Action _pressed;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _stop = new();
    private readonly long[] _nextTry = new long[4];

    /// <param name="pressed">Called (on the polling thread) each time the combination goes down.</param>
    public ControllerShortcut(Buttons combo, Action pressed)
    {
        _combo = combo;
        _pressed = pressed;
        _thread = new Thread(Poll) { IsBackground = true, Name = "Controller shortcut", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    /// <summary>True when this reading completes the combination and the previous one didn't (so holding it takes one shot).</summary>
    internal static bool JustPressed(Buttons combo, Buttons before, Buttons now) =>
        combo != Buttons.None && (now & combo) == combo && (before & combo) != combo;

    private void Poll()
    {
        var before = new Buttons[4];
        while (!_stop.Wait(16)) // about 60 checks a second: quick enough for a button press, next to nothing on the CPU
        {
            long now = Environment.TickCount64;
            for (uint pad = 0; pad < 4; pad++)
            {
                // Empty slots are only looked at once a second.
                if (now < _nextTry[pad]) continue;
                if (XInputGetState(pad, out var state) != 0)
                {
                    _nextTry[pad] = now + 1000;
                    before[pad] = Buttons.None;
                    continue;
                }
                var buttons = (Buttons)state.Gamepad.Buttons;
                if (JustPressed(_combo, before[pad], buttons))
                {
                    try { _pressed(); }
                    catch (Exception ex) { Log.Write($"Controller shortcut failed: {ex.Message}"); }
                    Buzz(pad);
                }
                before[pad] = buttons;
            }
        }
    }

    /// <summary>A short, light buzz so you know the shot was taken, like on a console.</summary>
    private static void Buzz(uint pad)
    {
        var on = new Vibration { Left = 20000, Right = 20000 };
        XInputSetState(pad, ref on);
        Thread.Sleep(90);
        var off = new Vibration();
        XInputSetState(pad, ref off);
    }

    public void Dispose()
    {
        _stop.Set();
        _thread.Join(500);
        _stop.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Gamepad { public ushort Buttons; public byte LeftTrigger, RightTrigger; public short ThumbLX, ThumbLY, ThumbRX, ThumbRY; }

    [StructLayout(LayoutKind.Sequential)]
    private struct State { public uint Packet; public Gamepad Gamepad; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Vibration { public ushort Left, Right; }

    [DllImport("xinput1_4.dll")] private static extern uint XInputGetState(uint pad, out State state);
    [DllImport("xinput1_4.dll")] private static extern uint XInputSetState(uint pad, ref Vibration vibration);
}
