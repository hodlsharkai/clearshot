namespace ClearShot.Tests;

public class SettingsTests
{
    [Fact]
    public void Missing_file_gives_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var s = Settings.Load(path);
        Assert.Equal("PrintScreen", s.FullScreenHotkey);
        Assert.Equal("Ctrl+PrintScreen", s.RegionHotkey);
        Assert.True(s.PlaySound);
        Assert.True(s.ShowPreview);
        Assert.False(string.IsNullOrWhiteSpace(s.SaveFolder));
    }

    [Fact]
    public void Corrupt_file_gives_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        File.WriteAllText(path, "{ not json");
        try { Assert.Equal("PrintScreen", Settings.Load(path).FullScreenHotkey); }
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
            s.Save(path);
            var loaded = Settings.Load(path);
            Assert.Equal(@"D:\Shots", loaded.SaveFolder);
            Assert.Equal("Ctrl+Shift+S", loaded.RegionHotkey);
            Assert.False(loaded.PlaySound);
        }
        finally { File.Delete(path); }
    }
}
