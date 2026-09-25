using System.Text.Encodings.Web;
using System.Text.Json;

namespace ClearShot;

internal sealed class Settings
{
    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClearShot");

    public static string DefaultPath { get; } = Path.Combine(DataFolder, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public string SaveFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ClearShot");

    public string FullScreenHotkey { get; set; } = "Alt+C";
    public string RegionHotkey { get; set; } = "Alt+Shift+C";
    public string GifHotkey { get; set; } = "Alt+G";
    public string EditHotkey { get; set; } = "Alt+Shift+E";
    public string GifEditHotkey { get; set; } = "Alt+Shift+G";
    /// <summary>A controller button combination that takes a full-screen screenshot, as well as the keyboard. Off by default.</summary>
    public string ControllerShortcut { get; set; } = "Off";
    public string GifQuality { get; set; } = "Standard";
    public bool SaveMp4 { get; set; }
    public bool PlaySound { get; set; } = true;
    public bool ShowPreview { get; set; } = true;
    public bool PauseMediaWhileSelecting { get; set; }
    public bool FreezeWhileSelecting { get; set; }
    public bool SaveHdrJxr { get; set; }
    public bool SaveHdrPng { get; set; }
    public string Theme { get; set; } = "System";
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
