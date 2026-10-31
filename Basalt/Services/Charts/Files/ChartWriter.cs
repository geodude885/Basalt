using System.Text.Json.Nodes;
using Basalt.Services.Charts.Model;
using Basalt.Services.Clips;

namespace Basalt.Services.Charts.Files;

public static class ChartWriter
{
    public const double ClipEndPadding = 1.0;

    public static string WriteLaneSetup(string chartText, ArenaSpec arena, Lane lane, IReadOnlyList<ActorSpec> actors)
    {
        var chart = ChartJson.Parse(chartText);
        chart["arena"] = ChartJson.ToNode(new ArenaDto
        {
            Map = arena.Map.ToString().ToLowerInvariant(),
            At = new PointDto { X = RoundPosition(arena.X), Z = RoundPosition(arena.Z), Facing = RoundPosition(arena.Facing) },
        });
        chart["lane"] = ChartJson.ToNode(new LaneDto { Length = RoundPosition(lane.Length), Width = RoundPosition(lane.Width) });
        chart["actors"] = ActorsJson(actors);
        return ChartJson.Write(chart);
    }

    public static string WriteActors(string chartText, IReadOnlyList<ActorSpec> actors)
    {
        var chart = ChartJson.Parse(chartText);
        chart["actors"] = ActorsJson(actors);
        return ChartJson.Write(chart);
    }

    public static string WritePerformances(string chartText, Chart chart, string actorId, Clip clip, double at, bool mime = false)
    {
        var chartJson = ChartJson.Parse(chartText);

        var performances = UpdateActorPerformance(chart,
            actorId,
            new PerformanceClip(Math.Round(at, 3), clip.Name, MathF.Round(clip.Start, 2)) { Mime = mime });

        var lastClipEnd = at + clip.Duration - clip.Start;

        chartJson["performances"] = ChartJson.ToNode(performances.Select(performance => new PerformanceDto
        {
            Actor = performance.ActorId,
            Clips = performance.Clips.Select(c => new PerformanceClipDto { T = c.Time, Clip = c.Clip, Start = c.Start, End = c.End, Mime = c.Mime ? true : null }).ToList(),
        }).ToList());

        var end = Math.Max(chart.End, RoundTime(lastClipEnd + ClipEndPadding));
        if (end > chart.End) chartJson["end"] = end;
        return ChartJson.Write(chartJson);
    }

    public static string ScaleChartOnLaneLengthChange(string chartText, float factor, float length)
    {
        if (Math.Abs(factor - 1f) < 0.001f) return chartText;

        var chart = ChartJson.Parse(chartText);
        ScaleAllNumbersByName("z", chart["events"], factor, 0, length + Lane.ProjectileReach);
        ScaleAllNumbersByName("length", chart["playerZone"], factor, 0.5, length);
        return ChartJson.Write(chart);
    }

    public static List<ActorPerformance> UpdateActorPerformance(Chart chart, string actorId, PerformanceClip clip) =>
        chart.Performances.Where(t => t.ActorId != actorId).Append(new ActorPerformance(actorId, [clip])).ToList();

    private static JsonNode? ActorsJson(IReadOnlyList<ActorSpec> actors) => ChartJson.ToNode(actors.Select(actor => new ActorDto
    {
        Id = actor.Id,
        Name = actor.Name,
        At = new PointDto { X = RoundPosition(actor.At.X), Z = RoundPosition(actor.At.Z), Facing = actor.At.Facing == 0f ? null : RoundPosition(actor.At.Facing) },
        Look = actor.Look,
        Shiftstones = actor.Shiftstones.Count == 0 ? null : actor.Shiftstones.ToList(),
        Measurement = actor.Measurement is { } size ? new MeasurementDto { Height = MathF.Round(size.Height, 3), ArmSpan = MathF.Round(size.ArmSpan, 3) } : null,
    }).ToList());

    private static void ScaleAllNumbersByName(string name, JsonNode? node, float factor, double least, double most)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var item in array) ScaleAllNumbersByName(name, item, factor, least, most);
                break;

            case JsonObject properties:
                foreach ((var key, var value) in properties.ToList())
                {
                    if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase) && value is JsonValue number && number.TryGetValue<double>(out var before))
                        properties[key] = Math.Round(Math.Clamp(before * factor, least, most), 2);
                    else
                        ScaleAllNumbersByName(name, value, factor, least, most);
                }

                break;
        }
    }

    private static float RoundPosition(float value) => MathF.Round(value, 2);

    private static double RoundTime(double seconds) => Math.Round(seconds, 3);
}
