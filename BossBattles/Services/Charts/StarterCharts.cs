namespace BossBattles.Services.Charts;

/// <summary>
/// Charts written into an empty chart folder, so there is something to play and something to copy
/// from. Each is loaded in the tests, so a starter chart can never ship broken.
/// </summary>
public static class StarterCharts
{
    public const string SelfTestName = "Self Test";

    /// <summary>The clip the self-test chart plays; the self-test supplies it, so it needs no file.</summary>
    public const string SelfTestClip = "selftest-sway";

    public static IReadOnlyList<(string File, string Json)> All { get; } = new[]
    {
        ("01-warm-up.json", WarmUp),
        ("02-crossfire.json", Crossfire),
        ("99-self-test.json", SelfTest),
    };

    /// <summary>Slow discs from changing places with long warnings: learn to read the lane.</summary>
    public const string WarmUp = /*lang=json*/ """
        {
          // Lane coordinates: x across (0 = centre), y up, z from you (0) to the far end (lane.length).
          "format": 1,
          "name": "Warm-up",
          "author": "BossBattles",
          "difficulty": "Easy",
          "description": "Slow discs with long warnings. Learn to read the lane.",
          // You stand 6 m from the Ring's centre and the lane runs north through it.
          "arena": { "map": "ring", "at": { "x": 0, "z": -6 }, "facing": 0 },
          "lane": { "length": 12, "width": 3 },
          "actors": [ { "id": "dummy", "name": "Training Dummy", "at": { "x": 0, "z": 12 } } ],
          "events": [
            { "t": 3,  "projectile": { "structure": "Disc", "from": { "x": 0,    "y": 1.4, "z": 10 }, "to": "player", "speed": 8, "telegraph": 1.2 } },
            { "t": 6,  "projectile": { "structure": "Disc", "from": { "x": -1.2, "y": 1.2, "z": 10 }, "to": "player", "speed": 8, "telegraph": 1.0 } },
            { "t": 9,  "projectile": { "structure": "Disc", "from": { "x": 1.2,  "y": 1.6, "z": 10 }, "to": "player", "speed": 9, "telegraph": 1.0 } },
            { "t": 12, "projectile": { "structure": "Ball", "from": { "x": 0,    "y": 1.0, "z": 9 },  "to": "player", "speed": 7, "telegraph": 1.0 } },
            { "t": 15, "projectile": { "structure": "Disc", "from": { "x": -1,   "y": 1.4, "z": 10 }, "to": { "x": 0.6, "y": 1.3 }, "speed": 9, "telegraph": 0.9 } },
            { "t": 16, "projectile": { "structure": "Disc", "from": { "x": 1,    "y": 1.4, "z": 10 }, "to": { "x": -0.6, "y": 1.3 }, "speed": 9, "telegraph": 0.9 } },
            { "t": 20, "move": "Straight" },
            { "t": 23, "projectile": { "structure": "Disc", "from": { "x": 0,    "y": 2.0, "z": 11 }, "to": "player", "speed": 10, "telegraph": 0.8 } },
            { "t": 24, "projectile": { "structure": "Disc", "from": { "x": 0,    "y": 0.8, "z": 11 }, "to": "player", "speed": 10, "telegraph": 0.8 } }
          ],
          "end": 28
        }
        """;

    /// <summary>Shots cutting across the lane at angles, a phase where the actor moves and hits are worth double.</summary>
    public const string Crossfire = /*lang=json*/ """
        {
          "format": 1,
          "name": "Crossfire",
          "author": "BossBattles",
          "difficulty": "Normal",
          "description": "Shots cutting across the lane. Hits on the Gunner count double while it is open.",
          // No spot: the lane is laid across the Pit through its centre.
          "arena": { "map": "pit" },
          "lane": { "length": 12, "width": 3 },
          "actors": [ { "id": "gunner", "name": "The Gunner", "at": { "x": 0, "z": 12 } } ],
          "phases": [
            { "name": "Angles", "start": 0 },
            { "name": "Open", "start": 18, "scoreMultiplier": 2, "at": { "x": 1.2, "z": 10 } },
            { "name": "Storm", "start": 26 }
          ],
          "events": [
            { "t": 2,    "projectile": { "structure": "Disc", "from": { "x": -1.5, "y": 1.4, "z": 10 }, "to": { "x": 1.2,  "y": 1.3 }, "speed": 11, "telegraph": 0.8, "by": "gunner" } },
            { "t": 3.5,  "projectile": { "structure": "Disc", "from": { "x": 1.5,  "y": 1.4, "z": 10 }, "to": { "x": -1.2, "y": 1.3 }, "speed": 11, "telegraph": 0.8, "by": "gunner" } },
            { "t": 5,    "projectile": { "structure": "Disc", "from": { "x": 0,    "y": 1.5, "z": 11 }, "to": "player", "speed": 12, "telegraph": 0.7, "by": "gunner" } },
            { "t": 7,    "projectile": { "structure": "Ball", "from": { "x": -1.2, "y": 1.0, "z": 9 },  "to": { "x": 0.8,  "y": 1.0 }, "speed": 9, "telegraph": 0.8, "by": "gunner" } },
            { "t": 7.4,  "projectile": { "structure": "Ball", "from": { "x": 1.2,  "y": 1.0, "z": 9 },  "to": { "x": -0.8, "y": 1.0 }, "speed": 9, "telegraph": 0.8, "by": "gunner" } },
            { "t": 10,   "at": { "x": -1.2, "z": 11 } },
            { "t": 11,   "projectile": { "structure": "Disc", "from": { "x": -1.2, "y": 1.4, "z": 10 }, "to": "player", "speed": 13, "telegraph": 0.6, "by": "gunner" } },
            { "t": 12,   "projectile": { "structure": "Disc", "from": { "x": -1.2, "y": 1.4, "z": 10 }, "to": { "x": 1.0, "y": 1.4 }, "speed": 13, "telegraph": 0.6, "by": "gunner" } },
            { "t": 14,   "move": "Straight" },
            { "t": 20,   "projectile": { "structure": "Disc", "from": { "x": 1.2,  "y": 1.4, "z": 9 },  "to": "player", "speed": 10, "telegraph": 0.8, "by": "gunner" } },
            { "t": 27,   "projectile": { "structure": "Disc", "from": { "x": -1.5, "y": 1.2, "z": 10 }, "to": "player", "speed": 14, "telegraph": 0.5 } },
            { "t": 27.5, "projectile": { "structure": "Disc", "from": { "x": 1.5,  "y": 1.6, "z": 10 }, "to": "player", "speed": 14, "telegraph": 0.5 } },
            { "t": 28,   "projectile": { "structure": "Disc", "from": { "x": 0,    "y": 1.0, "z": 10 }, "to": "player", "speed": 14, "telegraph": 0.5 } },
            { "t": 28.5, "projectile": { "structure": "Disc", "from": { "x": 0,    "y": 1.9, "z": 10 }, "to": "player", "speed": 14, "telegraph": 0.5 } }
          ],
          "end": 33
        }
        """;

    /// <summary>A few seconds touching every command type - what the automated self-test plays.</summary>
    public const string SelfTest = /*lang=json,strict*/ """
        {
          "format": 1,
          "name": "Self Test",
          "author": "BossBattles",
          "difficulty": "Test",
          "description": "A few seconds touching every command - what the automated self-test plays.",
          "zone": { "length": 6, "height": 2.5 },
          "rules": { "spawn": "discs", "cleanup": 2 },
          "colours": { "edges": "#33ccff", "ends": "#ff3399", "walls": "#66ccff33" },
          "lane": { "length": 10, "width": 3 },
          "actors": [ { "id": "probe", "name": "Self Test", "at": { "x": 0, "z": 10 }, "shiftstones": [ "Vigor", "Flow" ] } ],
          "phases": [ { "name": "Start", "start": 0 }, { "name": "Moved", "start": 3, "at": { "x": 1, "z": 9 }, "colours": { "edges": "#ffaa00", "walls": "none" } } ],
          "events": [
            { "t": 1.5, "projectile": { "structure": "Disc", "from": { "x": 0, "y": 1.4, "z": 8 }, "to": "player", "speed": 10, "telegraph": 0.5, "by": "probe" } },
            { "t": 2, "projectile": { "structure": "RockCube", "from": { "x": 1.2, "y": 0, "z": 8 }, "to": { "x": 1.4, "y": 1 }, "speed": 8, "telegraph": 0.5 } },
            { "t": 2.5, "projectile": { "structure": "Ball", "from": { "x": -1, "y": 1.2, "z": 8 }, "to": { "x": 0.5, "y": 1.2 }, "speed": 8, "telegraph": 0.5 } },
            { "t": 3.2, "projectile": { "structure": "RockCube", "from": { "x": -1.2, "y": 0, "z": 8 }, "to": { "x": -1.4, "y": 1 }, "speed": 8, "telegraph": 0.5 } },
            { "t": 4,   "move": "SpawnCube" },
            { "t": 5,   "at": { "x": 0, "z": 10 } },
            { "t": 5.5, "clip": "selftest-sway" }
          ],
          "end": 7
        }
        """;
}
