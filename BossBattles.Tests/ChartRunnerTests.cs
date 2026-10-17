using BossBattles.Services.Charts;
using Xunit;

namespace BossBattles.Tests;

public class ChartRunnerTests
{
    private static Chart Load(string json)
    {
        var result = ChartLoader.Load(json);
        Assert.True(result.Ok, string.Join("; ", result.Errors));
        return result.Chart!;
    }

    [Fact]
    public void ProjectilesSpawnTheirTelegraphEarlyThenLaunch()
    {
        var runner = new ChartRunner(Load(/*lang=json,strict*/ """
            { "events": [ { "t": 2, "projectile": { "structure": "Disc", "from": { "x": 0, "y": 1, "z": 10 }, "speed": 12, "telegraph": 0.5 } } ] }
            """));

        Assert.Collection(runner.Advance(0),
            c => Assert.IsType<EnterPhase>(c),
            c => Assert.IsType<PlaceActor>(c));

        Assert.Empty(runner.Advance(1.4));

        var spawn = Assert.IsType<SpawnProjectile>(Assert.Single(runner.Advance(1.5)));
        var launch = Assert.IsType<LaunchProjectile>(Assert.Single(runner.Advance(2.0)));
        Assert.Equal(spawn.Id, launch.Id);

        Assert.IsType<EndChart>(Assert.Single(runner.Advance(100)));
        Assert.True(runner.Finished);
    }

    [Fact]
    public void EveryActorIsPlacedAtTheStart()
    {
        var runner = new ChartRunner(Load(/*lang=json,strict*/ """
            { "actors": [ { "id": "a", "at": { "x": -1, "z": 12 } }, { "id": "b", "at": { "x": 1, "z": 11 } } ] }
            """));

        var places = runner.Advance(0).OfType<PlaceActor>().ToList();

        Assert.Equal(new[] { "a", "b" }, places.Select(p => p.ActorId));
        Assert.Equal(new Spot(1, 11), places[1].At);
    }

    [Fact]
    public void ABigStepReturnsEverythingDueInOrder()
    {
        var runner = new ChartRunner(Load(/*lang=json,strict*/ """
            {
              "events": [
                { "t": 4, "move": "Kick" },
                { "t": 3, "move": "Straight" },
                { "t": 3, "at": { "x": 1, "z": 10 } }
              ],
              "end": 10
            }
            """));

        Assert.Collection(runner.Advance(5),
            c => Assert.IsType<EnterPhase>(c),
            c => Assert.Equal(new Spot(0, 12), Assert.IsType<PlaceActor>(c).At),
            c => Assert.Equal(new Spot(1, 10), Assert.IsType<PlaceActor>(c).At),
            c => Assert.Equal("Straight", Assert.IsType<RunMove>(c).Stack),
            c => Assert.Equal("Kick", Assert.IsType<RunMove>(c).Stack));
    }

    [Fact]
    public void EachCommandIsReturnedOnce()
    {
        var runner = new ChartRunner(Load(/*lang=json,strict*/ """{ "events": [ { "t": 1, "move": "Kick" } ] }"""));

        runner.Advance(2);

        Assert.Empty(runner.Advance(2));
        Assert.Empty(runner.Advance(2.5));
    }

    [Fact]
    public void APhasesSpotIsAppliedWhenThePhaseStarts()
    {
        var runner = new ChartRunner(Load(/*lang=json,strict*/ """
            { "phases": [ { "start": 0 }, { "name": "Open", "start": 10, "at": { "x": 1, "z": 9 } } ], "end": 20 }
            """));

        runner.Advance(9);

        Assert.Collection(runner.Advance(10),
            c => Assert.Equal("Open", Assert.IsType<EnterPhase>(c).Phase.Name),
            c =>
            {
                var place = Assert.IsType<PlaceActor>(c);
                Assert.Equal(ChartLoader.DefaultActorId, place.ActorId);
                Assert.Equal(new Spot(1, 9), place.At);
            });

        Assert.Equal("Open", runner.Chart.Phases.Last(p => p.Start <= 15).Name);
        Assert.Equal("Phase 1", runner.Chart.Phases.Last(p => p.Start <= 9.99).Name);
    }

    [Fact]
    public void TheClockCannotGoBackwards()
    {
        var runner = new ChartRunner(Load("{}"));
        runner.Advance(5);

        Assert.Throws<ArgumentException>(() => runner.Advance(4));
    }
}
