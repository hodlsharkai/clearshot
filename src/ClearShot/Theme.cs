using System.Runtime.InteropServices;

namespace ClearShot;

/// <summary>Light, dark, or whatever Windows is set to. Builds on WinForms' dark mode and fills its gaps.</summary>
internal static class Theme
{
    public static readonly (string Value, string Label)[] Choices =
    [
        ("System", "Match Windows"),
        ("Light", "Light"),
        ("Dark", "Dark"),
    ];

    private static readonly Color Accent = Color.FromArgb(56, 189, 248);
    private static readonly Color DarkButton = Color.FromArgb(48, 48, 52);
    private static readonly Color DarkButtonHover = Color.FromArgb(62, 62, 68);
    private static readonly Color DarkBorder = Color.FromArgb(86, 86, 92);
    private static readonly Color DarkSelected = Color.FromArgb(28, 74, 96);

    public static bool IsDark
    {
        get
        {
#pragma warning disable WFO5001 // WinForms dark mode is marked experimental
            return Application.IsDarkModeEnabled;
#pragma warning restore WFO5001
        }
    }

    public static void Apply(string? theme)
    {
#pragma warning disable WFO5001
        Application.SetColorMode(theme switch
        {
            "Light" => SystemColorMode.Classic,
            "Dark" => SystemColorMode.Dark,
            _ => SystemColorMode.System,
        });
#pragma warning restore WFO5001
    }

    /// <summary>Call at the end of a form's constructor: dark title bar, readable links and dark buttons.</summary>
    public static void Style(Form form)
    {
        if (!IsDark) return;
        form.HandleCreated += (_, _) =>
        {
            int on = 1;
            DwmSetWindowAttribute(form.Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
        };
        StyleChildren(form);
    }

    private static void StyleChildren(Control parent)
    {
        foreach (Control c in parent.Controls)
        {
            switch (c)
            {
                case LinkLabel link:
                    link.LinkColor = Accent;
                    link.ActiveLinkColor = Accent;
                    link.VisitedLinkColor = Accent;
                    break;
                case Button button:
                    Flatten(button);
                    button.FlatAppearance.MouseOverBackColor = DarkButtonHover;
                    break;
                case RadioButton { Appearance: Appearance.Button } toggle:
                    Flatten(toggle);
                    toggle.FlatAppearance.CheckedBackColor = DarkSelected;
                    toggle.FlatAppearance.MouseOverBackColor = DarkButtonHover;
                    break;
            }
            StyleChildren(c);
        }
    }

    private static void Flatten(ButtonBase b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.BackColor = DarkButton;
        b.ForeColor = Color.White;
        b.FlatAppearance.BorderColor = DarkBorder;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
