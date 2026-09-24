namespace ClearShot;

internal static class AppInfo
{
    public const string Name = "ClearShot";
    public const string Version = "1.0.0";

    // Leave empty to hide the menu item.
    public const string DonateUrl = "";
    public const string RepoUrl = "";

    public static void OpenUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }
}
