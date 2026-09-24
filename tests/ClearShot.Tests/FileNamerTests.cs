namespace ClearShot.Tests;

public class FileNamerTests
{
    [Fact]
    public void Name_uses_sortable_date_and_time()
    {
        var name = FileNamer.BaseName(new DateTime(2026, 9, 24, 14, 3, 7));
        Assert.Equal("ClearShot 2026-09-24 14.03.07", name);
    }

    [Fact]
    public void Adds_counter_when_file_exists()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var when = new DateTime(2026, 9, 24, 14, 3, 7);
            var first = FileNamer.UniquePath(dir, when);
            File.WriteAllText(first, "x");
            var second = FileNamer.UniquePath(dir, when);
            Assert.EndsWith("ClearShot 2026-09-24 14.03.07.png", first);
            Assert.EndsWith("ClearShot 2026-09-24 14.03.07 (2).png", second);
        }
        finally { Directory.Delete(dir, true); }
    }
}
