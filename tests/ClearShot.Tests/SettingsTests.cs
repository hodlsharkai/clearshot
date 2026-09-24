namespace ClearShot.Tests;

public class SettingsTests
{
    [Fact]
    public void Missing_file_gives_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var s = Settings.Load(path);
        Assert.Equal("Alt+C", s.FullScreenHotkey);
        Assert.Equal("Alt+Shift+C", s.RegionHotkey);
        Assert.Equal("Alt+G", s.GifHotkey);
        Assert.True(s.PlaySound);
        Assert.True(s.ShowPreview);
        Assert.False(s.PauseMediaWhileSelecting);
        Assert.False(s.FreezeWhileSelecting);
        Assert.False(string.IsNullOrWhiteSpace(s.SaveFolder));
    }

    [Fact]
    public void Corrupt_file_gives_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        File.WriteAllText(path, "{ not json");
        try { Assert.Equal("Alt+C", Settings.Load(path).FullScreenHotkey); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var s = Settings.Load(path);
            s.SaveFolder = @"D:\Shots";
            s.RegionHotkey = "Ctrl+Shift+S";
            s.PlaySound = false;
            s.PauseMediaWhileSelecting = true;
            s.FreezeWhileSelecting = true;
            s.Save(path);
            var loaded = Settings.Load(path);
            Assert.Equal(@"D:\Shots", loaded.SaveFolder);
            Assert.Equal("Ctrl+Shift+S", loaded.RegionHotkey);
            Assert.False(loaded.PlaySound);
            Assert.True(loaded.PauseMediaWhileSelecting);
            Assert.True(loaded.FreezeWhileSelecting);
        }
        finally { File.Delete(path); }
    }
}
