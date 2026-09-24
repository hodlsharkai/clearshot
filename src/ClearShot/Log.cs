namespace ClearShot;

/// <summary>Small local log for diagnosing capture problems. Never leaves the PC.</summary>
internal static class Log
{
    private static readonly object Gate = new();
    private static string LogPath => Path.Combine(Settings.DataFolder, "clearshot.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Settings.DataFolder);
                var info = new FileInfo(LogPath);
                if (info.Exists && info.Length > 1_000_000) info.Delete();
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never break a capture.
        }
    }
}
