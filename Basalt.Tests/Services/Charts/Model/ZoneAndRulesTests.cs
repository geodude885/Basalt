using Basalt.Services.Charts.Files;
using Basalt.Services.Charts.Model;
using Xunit;

namespace Basalt.Tests.Services.Charts.Model;

public class ZoneAndRulesTests
{
    [Fact]
    public void WithoutAZoneThePlayerKeepsTwoMetresFromTheFarEnd()
    {
        var chart = ChartLoader.Load(/*lang=json,strict*/ """{ "lane": { "length": 12, "width": 3 } }""").Chart!;

        Assert.Equal(new PlayerZone(10f, PlayerZone.DefaultHeight), chart.PlayerZone);
        Assert.Equal(SpawnRule.All, chart.Rules.PlayerSpawns);
        Assert.Equal(new PlayerZone(1.5f, PlayerZone.DefaultHeight), ChartLoader.Load(/*lang=json,strict*/ """{ "lane": { "length": 3, "width": 3 } }""").Chart!.PlayerZone);
    }

    [Fact]
    public void AChartSetsItsZoneAndRules()
    {
        var chart = ChartLoader.Load(/*lang=json,strict*/ """{ "playerZone": { "length": 6, "height": 2.5 }, "rules": { "playerSpawns": "Discs" } }""").Chart!;

        Assert.Equal(new PlayerZone(6f, 2.5f), chart.PlayerZone);
        Assert.Equal(SpawnRule.Discs, chart.Rules.PlayerSpawns);
    }

    [Theory]
    [InlineData(/*lang=json,strict*/ """{ "playerZone": { "length": 20 } }""", "playerZone.length")]
    [InlineData(/*lang=json,strict*/ """{ "playerZone": { "height": 0.5 } }""", "playerZone.height")]
    [InlineData(/*lang=json,strict*/ """{ "rules": { "playerSpawns": "some" } }""", "rules.playerSpawns")]
    public void BadZonesAndRulesAreReported(string json, string expected) =>
        Assert.Contains(ChartLoader.Load(json).Errors, e => e.Contains(expected, StringComparison.Ordinal));

    [Fact]
    public void TheZoneGoesWithTheLane()
    {
        const string text = /*lang=json,strict*/ """{ "lane": { "length": 12, "width": 3 }, "playerZone": { "length": 8, "height": 3 } }""";
        var scaled = ChartWriter.ScaleChartOnLaneLengthChange(text, 0.75f, 9f);

        Assert.Equal(6f, ChartLoader.Load(scaled).Chart!.PlayerZone.Length);
    }
}
