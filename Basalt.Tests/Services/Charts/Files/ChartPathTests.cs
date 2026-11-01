using Basalt.Services.Charts.Files;
using Xunit;

namespace Basalt.Tests.Services.Charts.Files;

public class ChartPathTests
{
    [Theory]
    [InlineData("song.ogg", true)]
    [InlineData("music/song.ogg", true)]
    [InlineData("music\\song.ogg", true)]
    [InlineData("../song.ogg", false)]
    [InlineData("music/../../song.ogg", false)]
    [InlineData("/song.ogg", false)]
    [InlineData("C:/song.ogg", false)]
    [InlineData("C:song.ogg", false)]
    [InlineData("", false)]
    [InlineData("music//song.ogg", false)]
    public void OnlyPathsInsideTheFolderAreSafe(string path, bool safe) =>
        Assert.Equal(safe, ChartFiles.IsSafeRelative(path));

    [Fact]
    public void InsideResolvesWithinTheFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "charts");

        Assert.Equal(Path.GetFullPath(Path.Combine(folder, "a", "b.ogg")), ChartFiles.EnsureInsideFolder(folder, "a/b.ogg"));
        Assert.Null(ChartFiles.EnsureInsideFolder(folder, "../b.ogg"));
    }

    [Theory]
    [InlineData("song-abc", true)]
    [InlineData("song-abc/clips", true)]
    [InlineData("", false)]
    [InlineData("..", false)]
    [InlineData("../Cache2", false)]
    [InlineData("../Cache2/song-abc", false)]
    public void IsInsideFolderIsNeverTheFolderItselfOrASibling(string relative, bool inside)
    {
        var folder = Path.Combine(Path.GetTempPath(), "Cache");

        Assert.Equal(inside, ChartFiles.IsInsideFolder(folder, Path.Combine(folder, relative)));
    }

    [Fact]
    public void UndoCanBeUndoneSoNewerEditsSurviveIt()
    {
        var file = Path.Combine(Path.GetTempPath(), $"Basalt-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(file, "old");
            ChartFiles.BackupAndSave(file, "old", "new");
            File.WriteAllText(file, "edited outside the game");

            Assert.True(ChartFiles.RestoreBackup(file));
            Assert.Equal("old", File.ReadAllText(file));

            Assert.True(ChartFiles.RestoreBackup(file));
            Assert.Equal("edited outside the game", File.ReadAllText(file));
        }
        finally
        {
            File.Delete(file);
            File.Delete(file + ".bak");
        }
    }

    [Fact]
    public void IsInsideFolderRefusesAnotherDrive() =>
        Assert.False(ChartFiles.IsInsideFolder(@"C:\Game\Cache", @"D:\Game\Cache\song-abc"));
}
