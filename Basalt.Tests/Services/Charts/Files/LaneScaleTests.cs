using Basalt.Services.Charts.Files;
using Basalt.Services.Charts.Model;
using Xunit;

namespace Basalt.Tests.Services.Charts.Files;

public class LaneScaleTests
{
    [Fact]
    public void ShorteningTheLaneTakesTheChartWithIt()
    {
        var text = /*lang=json,strict*/ """
            { "lane": { "length": 12, "width": 3 }, "actors": [ { "id": "boss", "at": { "x": 0, "z": 12 } } ], "events": [
              { "t": 1, "projectile": { "structure": "Disc", "from": { "x": 0, "y": 1.4, "z": 10 }, "speed": 8 } },
              { "t": 2, "at": { "x": -1, "z": 11 } } ] }
            """;
        var before = ChartLoader.Load(text).Chart!;
        (var lane, var actors) = before.Lane.Resize(before.Actors, 9f, before.Lane.Width);

        var saved = ChartWriter.ScaleChartOnLaneLengthChange(ChartWriter.WriteLaneSetup(text, new ArenaSpec(ArenaKind.Pit, 1.7f, -7.2f, 354f), lane, actors), 9f / 12f, 9f);
        var after = ChartLoader.Load(saved);

        Assert.True(after.Ok, string.Join("; ", after.Errors));
        Assert.All(after.Chart!.Events.OfType<ProjectileEvent>(), p => Assert.InRange(p.From.Z, 0.1f, 9f));
        Assert.Equal(7.5f, after.Chart.Events.OfType<ProjectileEvent>().First().From.Z, 2);
        Assert.Equal(8.25f, after.Chart.Events.OfType<PlaceEvent>().Single().At.Z, 2);
    }
}
