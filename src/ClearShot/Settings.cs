using System.Text.Json;

namespace ClearShot;

internal sealed class Settings
{
    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClearShot");

    public static string DefaultPath { get; } = Path.Combine(DataFolder, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string SaveFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ClearShot");

    public string FullScreenHotkey { get; set; } = "PrintScreen";
    public string RegionHotkey { get; set; } = "Ctrl+PrintScreen";
    public bool PlaySound { get; set; } = true;
    public bool ShowPreview { get; set; } = true;
    public bool PauseMediaWhileSelecting { get; set; } = true;
    public bool WelcomeShown { get; set; }

    public static Settings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), JsonOptions);
                if (loaded is not null)
                {
                    if (string.IsNullOrWhiteSpace(loaded.SaveFolder)) loaded.SaveFolder = new Settings().SaveFolder;
                    return loaded;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Write($"Settings unreadable, using defaults: {ex.Message}");
        }
        return new Settings();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }
}
