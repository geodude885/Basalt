using Basalt.Services.Charts;
using Basalt.Services.Scoring;
using Xunit;

namespace Basalt.Tests;

public class ScoreKeeperTests
{
    [Fact]
    public void ACleanSurvivalIsAnS()
    {
        var score = new ScoreKeeper("Warm-up");

        var result = score.Finish(survived: true, seconds: 28, battleLength: 28);

        Assert.Equal("S", result.Rank);
        Assert.Equal(ScoreKeeper.SurvivalPoints, result.Score);
    }

    [Fact]
    public void ActorDamageIsABonusScaledByThePhase()
    {
        var score = new ScoreKeeper("Crossfire");

        score.ActorDamaged(3, multiplier: 1);
        score.ActorDamaged(2, multiplier: 2);
        score.PlayerDamaged(2);

        var result = score.Finish(survived: true, seconds: 33, battleLength: 33);

        Assert.Equal(7, result.ActorDamage, 6);
        Assert.Equal(
            ScoreKeeper.SurvivalPoints + (7 * ScoreKeeper.ActorDamagePoints) - (2 * ScoreKeeper.DamageTakenPoints),
            result.Score);
        Assert.Equal("A", result.Rank);
    }

    [Fact]
    public void NotSurvivingEarnsAShareOfHalfTheSurvivalPoints()
    {
        var score = new ScoreKeeper("Crossfire");
        score.PlayerDamaged(20);

        var result = score.Finish(survived: false, seconds: 10, battleLength: 40);

        Assert.Equal("D", result.Rank);
        Assert.Equal(1, result.HitsTaken);
        Assert.Equal(0, result.Score); // 125 for lasting a quarter, minus 500 for the damage, floored at 0
    }

    [Theory]
    [InlineData(true, 0, "S")]
    [InlineData(true, 3, "A")]
    [InlineData(true, 4, "B")]
    [InlineData(true, 8, "B")]
    [InlineData(true, 9, "C")]
    [InlineData(false, 0, "D")]
    public void RankIsAboutStayingClean(bool survived, int damage, string rank) =>
        Assert.Equal(rank, ScoreKeeper.Rank(survived, damage));
}

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
