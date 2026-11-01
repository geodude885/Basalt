using Basalt.Services.Charts;
using Basalt.Services.Charts.Files;
using Xunit;

namespace Basalt.Tests.Services.Charts;

public sealed class ChartLibraryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"Basalt-{Guid.NewGuid():N}");

    [Fact]
    public void AnEmptyFolderStaysEmpty()
    {
        var library = new ChartLibrary(_folder);
        library.Reload();

        Assert.Empty(library.Entries);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));
    }

    [Fact]
    public void ALooseJsonFileIsNotAChart()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "loose.json"), /*lang=json,strict*/ """{ "name": "Loose" }""");
        var library = new ChartLibrary(_folder);
        library.Reload();

        Assert.Empty(library.Entries);
    }

    [Fact]
    public void ChartsSharingANameAreOneSongWithItsDifficultiesHardestFirst()
    {
        Directory.CreateDirectory(_folder);
        WriteChart("a-hard", /*lang=json,strict*/ """{ "name": "Song", "difficulty": "Hard" }""");
        WriteChart("b-other", /*lang=json,strict*/ """{ "name": "Other" }""");
        WriteChart("c-easy", /*lang=json,strict*/ """{ "name": "song", "difficulty": "easy" }""");
        WriteChart("d-wild", /*lang=json,strict*/ """{ "name": "Song", "difficulty": "Wild" }""");
        WriteChart("e-none", /*lang=json,strict*/ """{ "name": "Song" }""");
        var library = new ChartLibrary(_folder);
        library.Reload();

        Assert.Equal(new[] { "a-hard", "b-other" }, library.Songs.Select(e => e.Stem));
        Assert.Equal(new[] { "a-hard", "c-easy", "d-wild", "e-none" }, library.DifficultiesOf(library.Entries[0]).Select(e => e.Stem));
        Assert.Single(library.DifficultiesOf(library.Entries[1]));
    }

    [Fact]
    public void ABrokenChartIsListedWithItsErrorsButNotPlayable()
    {
        Directory.CreateDirectory(_folder);
        WriteChart("broken", /*lang=json,strict*/ """{ "events": [ { "t": 1 } ] }""");

        var library = new ChartLibrary(_folder);
        library.Reload();

        var entry = Assert.Single(library.Entries);
        Assert.Equal("broken", entry.Name);
        Assert.False(entry.Result.Ok);
        Assert.Empty(library.Playable);
    }

    [Fact]
    public void EachDifficultyHasItsOwnBest()
    {
        var easy = Entry(/*lang=json,strict*/ """{ "name": "Song", "author": "me", "difficulty": "Easy" }""");
        var hard = Entry(/*lang=json,strict*/ """{ "name": "Song", "author": "me", "difficulty": "Hard" }""");

        Assert.NotEqual(easy.ScoreKey, hard.ScoreKey);
    }

    private static ChartEntry Entry(string json) => new("chart.json", ChartLoader.Load(json), ChartSource.Folder);

    private void WriteChart(string name, string json)
    {
        var folder = Path.Combine(_folder, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, ChartLibrary.FolderChartFile), json);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
