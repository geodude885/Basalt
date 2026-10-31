namespace Basalt.DevTools.Testing;

/// <summary>The chart the automated self-test plays: a few seconds touching every command.</summary>
public static class SelfTestChart
{
    public const string Name = "Self Test";

    /// <summary>The clip it plays, supplied in memory by the self-test.</summary>
    public const string Clip = "selftest-sway";

    public const string Json = /*lang=json,strict*/ """
        {
          "format": 3,
          "name": "Self Test",
          "author": "Basalt",
          "difficulty": "Test",
          "description": "A few seconds touching every command - what the automated self-test plays.",
          "playerZone": { "length": 6, "height": 2.5 },
          "rules": { "playerSpawns": "discs", "breakStillAfter": 2 },
          "colours": { "sideLines": "#33ccff", "endLines": "#ff3399", "walls": "#66ccff33" },
          "lane": { "length": 10, "width": 3 },
          "actors": [ { "id": "probe", "name": "Self Test", "at": { "x": 0, "z": 10 }, "shiftstones": [ "Vigor", "Flow" ] } ],
          "phases": [ { "name": "Start", "t": 0 }, { "name": "Moved", "t": 3, "colours": { "sideLines": "#ffaa00", "walls": "none" } } ],
          "events": [
            { "t": 1.5, "projectile": { "structure": "Disc", "from": { "x": 0, "y": 1.4, "z": 8 }, "speed": 10, "telegraph": 0.5 } },
            { "t": 2, "projectile": { "structure": "RockCube", "from": { "x": 1.2, "y": 0, "z": 8 }, "to": { "x": 1.4, "y": 1 }, "speed": 8, "telegraph": 0.5 } },
            { "t": 2.5, "projectile": { "structure": "Ball", "from": { "x": -1, "y": 1.2, "z": 8 }, "to": { "x": 0.5, "y": 1.2 }, "speed": 8, "telegraph": 0.5 } },
            { "t": 3,   "at": { "x": 1, "z": 9 } },
            { "t": 3.2, "projectile": { "structure": "RockCube", "from": { "x": -1.2, "y": 0, "z": 8 }, "to": { "x": -1.4, "y": 1 }, "speed": 8, "telegraph": 0.5 } },
            { "t": 4,   "move": "SpawnCube" },
            { "t": 5,   "at": { "x": 0, "z": 10 } }
          ],
          "performances": [ { "actor": "probe", "clips": [ { "t": 5.5, "clip": "selftest-sway" } ] } ],
          "end": 7
        }
        """;
}
