using BossBattles.Services.Charts;
using BossBattles.Services.Scoring;
using Xunit;

namespace BossBattles.Tests;

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
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"BossBattles-{Guid.NewGuid():N}");

    [Fact]
    public void AnEmptyFolderGetsTheStarterCharts()
    {
        var library = new ChartLibrary(_folder);

        Assert.Equal(StarterCharts.All.Count, library.Reload());
        Assert.Equal(StarterCharts.All.Count, library.Playable.Count());
        Assert.NotNull(library.Find("Warm-up"));
    }

    [Fact]
    public void AFolderWithChartsIsNeverWrittenTo()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "mine.json"), /*lang=json,strict*/ """{ "name": "Mine" }""");

        var library = new ChartLibrary(_folder);

        Assert.Equal(0, library.Reload());
        Assert.Equal("Mine", Assert.Single(library.Entries).Name);
    }

    [Fact]
    public void UneditedStarterChartsAreUpdatedAndEditedOnesAreNot()
    {
        var library = new ChartLibrary(_folder);
        library.Reload();
        (var first, var firstJson) = StarterCharts.All[0];
        (var second, _) = StarterCharts.All[1];

        // Pretend an older mod wrote them: rewrite both and the record, then edit the second.
        File.WriteAllText(Path.Combine(_folder, first), /*lang=json,strict*/ "{ \"name\": \"Old Warm-up\" }");
        File.WriteAllText(Path.Combine(_folder, second), /*lang=json,strict*/ "{ \"name\": \"Old Crossfire\" }");
        var record = Path.Combine(_folder, ".starters");
        File.WriteAllLines(record, File.ReadAllLines(record).Select(l => l.EndsWith(first, StringComparison.Ordinal) ? $"{Hash(/*lang=json,strict*/ "{ \"name\": \"Old Warm-up\" }")} {first}" : l));

        Assert.Equal(1, library.Reload());
        Assert.Equal(firstJson, File.ReadAllText(Path.Combine(_folder, first)));
        Assert.Contains("Old Crossfire", File.ReadAllText(Path.Combine(_folder, second)), StringComparison.Ordinal);
        Assert.DoesNotContain(library.Entries, e => e.File.EndsWith(".starters", StringComparison.Ordinal));
    }

    private static string Hash(string text) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n"))))[..16];

    [Fact]
    public void ABrokenChartIsListedWithItsErrorsButNotPlayable()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "broken.json"), /*lang=json,strict*/ """{ "events": [ { "t": 1 } ] }""");

        var library = new ChartLibrary(_folder);
        library.Reload();

        var entry = Assert.Single(library.Entries);
        Assert.Equal("broken", entry.Name);
        Assert.False(entry.Result.Ok);
        Assert.Empty(library.Playable);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
