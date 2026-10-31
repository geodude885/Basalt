namespace Basalt.Services.Charts;

public static class ChartTemplate
{
    // Creates new chart with player look and shiftstones
    public static string Create(string chartsFolder, string name, string? author, string? look = null, IReadOnlyList<string>? stones = null)
    {
        var stem = ChartFiles.SanitizeFileStem(name).ToLowerInvariant();
        var folder = Path.Combine(chartsFolder, ChartFiles.UniqueFileName(stem, "-", candidate => Directory.Exists(Path.Combine(chartsFolder, candidate))));

        Directory.CreateDirectory(Path.Combine(folder, ChartPackage.ClipsFolder));
        File.WriteAllText(Path.Combine(folder, ChartLibrary.FolderChartFile), Json(name, author, look, stones));
        return folder;
    }

    public static string Json(string name, string? author, string? look = null, IReadOnlyList<string>? stones = null) => $$"""
        {
          "format": {{ChartLoader.NewestFormat}},
          "name": {{Quote(name)}},
          "author": {{Quote(author ?? "you")}},
          "arena": { "map": "ring", "at": { "x": 0, "z": -6, "facing": 0 } },
          "lane": { "length": 12, "width": 3 },
          "actors": [ { "id": "boss", "name": "Boss", "at": { "x": 0, "z": 12 }, "look": {{Quote(look ?? "you")}}, "shiftstones": [{{string.Join(", ", (stones ?? []).Select(Quote))}}] } ],
          "events": []
        }
        """;

    private static string Quote(string text) => System.Text.Json.JsonSerializer.Serialize(text);
}
