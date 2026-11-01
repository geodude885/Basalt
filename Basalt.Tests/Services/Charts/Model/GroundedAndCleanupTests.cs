using Basalt.Services.Charts.Files;
using Basalt.Services.Charts.Model;
using Xunit;

namespace Basalt.Tests.Services.Charts.Model;

public class GroundedAndCleanupTests
{
    [Fact]
    public void PillarsCubesAndWallsAreGroundedUnlessToldOtherwise()
    {
        var chart = ChartLoader.Load(/*lang=json,strict*/ """
            { "events": [
              { "t": 1, "projectile": { "structure": "Wall", "from": { "x": 0, "y": 0, "z": 5 }, "speed": 8 } },
              { "t": 2, "projectile": { "structure": "RockCube", "from": { "x": 0, "y": 0, "z": 5 }, "speed": 8, "grounded": false } },
              { "t": 3, "projectile": { "structure": "Ball", "from": { "x": 0, "y": 1, "z": 5 }, "speed": 8 } },
              { "t": 4, "projectile": { "structure": "Ball", "from": { "x": 0, "y": 1, "z": 5 }, "speed": 8, "grounded": true } }
            ] }
            """).Chart!;

        Assert.Equal(new[] { true, false, false, true }, chart.Events.OfType<ProjectileEvent>().Select(p => p.IsGrounded));
    }

    [Fact]
    public void DiscsPillarsCubesAndWallsComeFromTheFloorAndAnythingGroundedRestsOnIt()
    {
        var chart = ChartLoader.Load(/*lang=json,strict*/ """
            { "events": [
              { "t": 1, "projectile": { "structure": "Disc", "from": { "x": 0, "y": 1.4, "z": 5 }, "speed": 8 } },
              { "t": 2, "projectile": { "structure": "RockCube", "from": { "x": 0, "y": 1, "z": 5 }, "speed": 8, "grounded": false } },
              { "t": 3, "projectile": { "structure": "Ball", "from": { "x": 0, "y": 1, "z": 5 }, "speed": 8 } },
              { "t": 4, "projectile": { "structure": "Ball", "from": { "x": 0, "y": 1, "z": 5 }, "speed": 8, "grounded": true } }
            ] }
            """).Chart!;
        var projectiles = chart.Events.OfType<ProjectileEvent>().ToList();

        Assert.Equal(new[] { true, true, false, false }, projectiles.Select(p => p.SpawnsFromFloor));
        Assert.Equal(new[] { true, true, false, true }, projectiles.Select(p => p.RestsOnFloor));
    }

    [Fact]
    public void CleanupDefaultsToThreeSecondsAndCanBeOff()
    {
        Assert.Equal(3, ChartLoader.Load("{}").Chart!.Rules.BreakStillAfterSeconds);
        Assert.Equal(0, ChartLoader.Load(/*lang=json,strict*/ """{ "rules": { "breakStillAfter": 0 } }""").Chart!.Rules.BreakStillAfterSeconds);
        Assert.Equal(SpawnRule.Discs, ChartLoader.Load(/*lang=json,strict*/ """{ "rules": { "playerSpawns": "discs", "breakStillAfter": 5 } }""").Chart!.Rules.PlayerSpawns);
        Assert.False(ChartLoader.Load(/*lang=json,strict*/ """{ "rules": { "breakStillAfter": -1 } }""").Ok);
    }

    [Fact]
    public void BreakingBehindIsOffUnlessAsked()
    {
        Assert.False(ChartLoader.Load("{}").Chart!.Rules.BreakBehind);
        Assert.False(ChartLoader.Load(/*lang=json,strict*/ """{ "rules": { "breakStillAfter": 5 } }""").Chart!.Rules.BreakBehind);

        var rules = ChartLoader.Load(/*lang=json,strict*/ """{ "rules": { "breakBehind": true } }""").Chart!.Rules;
        Assert.True(rules.BreakBehind);
        Assert.Equal(Rules.DefaultBreakStillAfterSeconds, rules.BreakStillAfterSeconds);
    }
}
