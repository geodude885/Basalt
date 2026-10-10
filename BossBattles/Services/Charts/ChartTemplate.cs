namespace BossBattles.Services.Charts;

/// <summary>
/// A new chart to start from: a folder in the chart folder holding a <c>chart.json</c> that plays
/// as it is and says, in comments, what each part does and how to change it - so a first chart is
/// editing one that works rather than writing one from nothing.
/// </summary>
public static class ChartTemplate
{
    /// <summary>Creates <c>&lt;charts&gt;/&lt;stem&gt;/chart.json</c> (a fresh folder name when taken); returns its folder.</summary>
    /// <param name="chartsFolder">The chart folder to make it in.</param>
    /// <param name="name">Its name, also its folder's.</param>
    /// <param name="author">Who made it; null for "you".</param>
    /// <param name="look">The maker's character string, for the boss to wear; null for whoever plays.</param>
    /// <param name="stones">The maker's equipped stones, for the boss; null for whoever plays.</param>
    public static string Create(string chartsFolder, string name, string? author, string? look = null, IReadOnlyList<string>? stones = null)
    {
        var stem = ChartPackage.FileStem(name).ToLowerInvariant();
        var folder = Path.Combine(chartsFolder, stem);
        for (var n = 2; Directory.Exists(folder); n++) folder = Path.Combine(chartsFolder, $"{stem}-{n}");

        Directory.CreateDirectory(Path.Combine(folder, ChartPackage.ClipsFolder));
        File.WriteAllText(Path.Combine(folder, ChartLibrary.FolderChartFile), Json(name, author, look, stones));
        return folder;
    }

    /// <summary>The template's text - a valid chart, tested.</summary>
    public static string Json(string name, string? author, string? look = null, IReadOnlyList<string>? stones = null) => $$"""
        {
          // A BossBattles chart. Save this file and the game reloads it - pick it on the chart picker
          // and press START. The full format: docs/chart-format.md in the mod's repository.
          "format": 1,
          "name": {{Quote(name)}},
          "author": {{Quote(author ?? "you")}},
          "difficulty": "Easy",
          "description": "My first chart.",

          // Where it is played. "here" is wherever you stand; "ring", "pit" or "gym" take you there.
          // Easiest: turn on Chart editing in the settings and press SETUP on the picker - walk the
          // lane, resize it, and SAVE writes this line (and "lane" and "actors") for you.
          "arena": { "map": "here" },

          // A song and a cover image, put beside this file - uncomment to use them. Songs: MP3, OGG or WAV.
          // "music": { "file": "song.mp3", "start": 0, "volume": 1 },
          // "cover": "cover.png",

          // With a bpm you can time events in beats ("beat": 4) instead of seconds ("t": 2). A song
          // that changes tempo: "bpm": [ { "beat": 0, "bpm": 120 }, { "beat": 64, "bpm": 140 } ].
          "bpm": 120,

          // The lane runs from you (z = 0) to the far end (z = length). x is across: negative left.
          "lane": { "length": 12, "width": 3 },

          // Where the player may go: up to "length" down the lane and "height" up. Without it: 2 m short
          // of the far end, 3.5 m high. "rules": what they may spawn - "all", "discs" or "none".
          "zone": { "length": 10, "height": 3.5 },
          "rules": { "spawn": "all" },

          // Who stands in the lane. "look" and "shiftstones" are yours as you made this chart, so
          // everyone fights the boss you dressed; "you" for either gives it whoever plays.
          "actors": [ { "id": "boss", "name": "Boss", "at": { "x": 0, "z": 12 }, "look": {{Quote(look ?? "you")}}, "shiftstones": {{(stones == null ? "\"you\"" : "[" + string.Join(", ", stones.Select(Quote)) + "]")}} } ],

          "events": [
            // A projectile: it comes up out of the floor at "from" "telegraph" seconds before it is
            // thrown, then flies at "speed" towards "to" - "player" (you) or a point { "x", "y" }.
            { "beat": 4,  "projectile": { "structure": "Disc", "from": { "x": 0,  "y": 1.4, "z": 10 }, "to": "player", "speed": 8, "telegraph": 1.0, "by": "boss" } },
            { "beat": 8,  "projectile": { "structure": "Disc", "from": { "x": -1, "y": 1.2, "z": 10 }, "to": { "x": 0.8, "y": 1.3 }, "speed": 9, "telegraph": 1.0 } },
            { "beat": 12, "projectile": { "structure": "Ball", "from": { "x": 1,  "y": 1.0, "z": 9 },  "to": "player", "speed": 8, "telegraph": 1.0 } },

            // The boss does one of its own moves...
            { "beat": 16, "move": "Straight" },

            // ...walks to another spot...
            { "beat": 18, "at": { "x": -1, "z": 11 } },

            // ...or plays a recording of you: Ctrl+Shift+R records, and the clip's name goes here.
            // { "beat": 20, "clip": "my-recording" },

            { "beat": 22, "projectile": { "structure": "Disc", "from": { "x": -1, "y": 1.4, "z": 10 }, "to": "player", "speed": 10, "telegraph": 0.8, "by": "boss" } }
          ],

          // You have survived at 16 seconds (32 beats at 120 bpm). Without "end": 3 s after the last event.
          "end": 16
        }
        """;

    private static string Quote(string text) => System.Text.Json.JsonSerializer.Serialize(text);
}
