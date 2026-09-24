using System.Globalization;

namespace ClearShot;

internal static class FileNamer
{
    public static string BaseName(DateTime when) =>
        "ClearShot " + when.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture);

    /// <summary>A path in <paramref name="folder"/> that does not exist yet, adding " (2)", " (3)" and so on if needed.</summary>
    public static string UniquePath(string folder, DateTime when)
    {
        var baseName = BaseName(when);
        var path = Path.Combine(folder, baseName + ".png");
        for (int n = 2; File.Exists(path); n++)
            path = Path.Combine(folder, $"{baseName} ({n}).png");
        return path;
    }
}
