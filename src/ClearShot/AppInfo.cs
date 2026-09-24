namespace ClearShot;

internal static class AppInfo
{
    public const string Name = "ClearShot";
    /// <summary>From the build (release builds set it from the git tag), without any "+commit" suffix.</summary>
    public static string Version { get; } =
        (typeof(AppInfo).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "1.0.0")
        .Split('+')[0];

    // Leave empty to hide the menu item.
    public const string DonateUrl = "";
    public const string RepoUrl = "https://github.com/rafflerobot/ClearShot";

    public static void OpenUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }
}
