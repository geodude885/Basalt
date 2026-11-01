using Basalt.Services.Charts.Files;
using Basalt.Services.Charts.Model;
using Xunit;

namespace Basalt.Tests.Services.Charts.Files;

public class LaneSetupTests
{
    [Fact]
    public void LaneSetupSavesArenaLaneAndActorsAndKeepsTheRest()
    {
        const string text = /*lang=json*/ """
            {
              // keep
              "name": "Setup",
              "lane": { "length": 12, "width": 3 },
              "actors": [ { "id": "boss", "at": { "x": 0, "z": 12 } } ],
              "events": [ { "t": 1, "move": "Straight" } ]
            }
            """;
        var chart = ChartLoader.Load(text).Chart!;
        (var lane, var actors) = chart.Lane.Resize(chart.Actors, 14f, 3.5f);

        var saved = ChartWriter.WriteLaneSetup(text, new ArenaSpec(ArenaKind.Gym, 1.25f, -3f, 90f), lane, actors);
        var after = ChartLoader.Load(saved).Chart!;

        Assert.Equal(new ArenaSpec(ArenaKind.Gym, 1.25f, -3f, 90f), after.Arena);
        Assert.Equal(new Lane(14f, 3.5f), after.Lane);
        Assert.Equal(14f, after.Actors[0].At.Z);
        Assert.Single(after.Events);
    }

    [Fact]
    public void ResizingIsClampedAndLeavesMidLaneActors()
    {
        var actors = new[] { new ActorSpec("a", "A", new Spot(0, 12), "you"), new ActorSpec("b", "B", new Spot(1, 6), "you") };

        (var lane, var moved) = new Lane(12, 3).Resize(actors, 100f, 0.1f);

        Assert.Equal(new Lane(Lane.MaxLength, Lane.MinWidth), lane);
        Assert.Equal(Lane.MaxLength, moved[0].At.Z);
        Assert.Equal(6f, moved[1].At.Z);
    }
}
