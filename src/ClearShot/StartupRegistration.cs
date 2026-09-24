using Microsoft.Win32;

namespace ClearShot;

/// <summary>Start with Windows, via the current user's Run key. No admin rights needed.</summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Passed when Windows starts ClearShot at sign-in, so it stays in the tray without opening its window.</summary>
    public const string StartupArgument = "--startup";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(AppInfo.Name) is string value && value.Contains(Environment.ProcessPath ?? "\0", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(AppInfo.Name, $"\"{Environment.ProcessPath}\" {StartupArgument}");
        else key.DeleteValue(AppInfo.Name, throwOnMissingValue: false);
    }
}
