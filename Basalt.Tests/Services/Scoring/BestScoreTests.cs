using Basalt.Services;
using Basalt.Services.Scoring;
using Xunit;

namespace Basalt.Tests.Services.Scoring;

public sealed class BestScoreTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"Basalt-{Guid.NewGuid():N}", "scores.json");

    [Fact]
    public void KeepsOnlyTheBestAndSurvivesARestart()
    {
        var store = new JsonFileRepository<BestScore>(_file);
        store.Load();
        var best = BestScore.From(Result(900, "A"), new DateTime(2026, 1, 1));
        store.Set("Song|me", best);

        Assert.False(best.IsBeatenBy(Result(500, "C")));
        Assert.True(best.IsBeatenBy(Result(1000, "S")));

        var again = new JsonFileRepository<BestScore>(_file);
        again.Load();
        Assert.Equal(best, again.Get("Song|me"));
        Assert.Null(again.Get("Other|me"));
    }

    [Fact]
    public void AnUnreadableFileStartsAFreshStore()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, "{ not json");

        var store = new JsonFileRepository<BestScore>(_file);
        store.Load();

        Assert.NotNull(store.LoadError);
        Assert.Null(store.Get("Song|me"));
    }

    public void Dispose()
    {
        var folder = Path.GetDirectoryName(_file);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private static BattleResult Result(int score, string rank) => new("Song", true, 30, 0, 0, 0, 10, score, rank);
}
