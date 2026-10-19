using System.Text.Json;
using System.Text.Json.Nodes;

namespace Basalt.Services.Charts;

// What lane setup saves into a chart: its arena, lane and actors.
public static class LaneSetupWriter
{
    public const float MinLength = 3f;
    public const float MaxLength = 40f;
    public const float MinWidth = 1f;
    public const float MaxWidth = 8f;

    public static string Write(string chartText, ArenaSpec arena, Lane lane, IReadOnlyList<ActorSpec> actors)
    {
        var chart = ChartJson.Parse(chartText);
        chart["arena"] = ChartJson.ToNode(new ArenaDto
        {
            Map = arena.Map.ToString().ToLowerInvariant(),
            At = arena.HasSpot ? new PointDto { X = Round(arena.X!.Value), Z = Round(arena.Z!.Value) } : null,
            Facing = arena.Facing is { } facing ? Round(facing) : null,
        });
        chart["lane"] = ChartJson.ToNode(new LaneDto { Length = Round(lane.Length), Width = Round(lane.Width) });
        chart["actors"] = ActorsJson(actors);
        return ChartJson.Write(chart);
    }

    public static string WriteActors(string chartText, IReadOnlyList<ActorSpec> actors)
    {
        var chart = ChartJson.Parse(chartText);
        chart["actors"] = ActorsJson(actors);
        return ChartJson.Write(chart);
    }

    // Every "z" in the events and phases, and the zone's length, scaled by `factor`, so the chart keeps its shape on a
    // resized lane.
    public static string ScaleDepth(string chartText, float factor, float length)
    {
        if (Math.Abs(factor - 1f) < 0.001f) return chartText;

        // Projectiles may start past the far end; actor spots stay in the lane, and so does your zone.
        var chart = ChartJson.Parse(chartText);
        ScaleNumbersNamed("z", chart["events"], factor, 0, length + ChartLoader.ProjectileReach);
        ScaleNumbersNamed("z", chart["phases"], factor, 0, length);
        ScaleNumbersNamed("length", chart["zone"], factor, 0.5, length);
        return ChartJson.Write(chart);
    }

    // A lane resized within limits; actors at the old far end, or past the new one, move to the new far end.
    public static (Lane Lane, IReadOnlyList<ActorSpec> Actors) Resize(Lane lane, IReadOnlyList<ActorSpec> actors, float length, float width)
    {
        var resized = new Lane(Math.Clamp(Round(length), MinLength, MaxLength), Math.Clamp(Round(width), MinWidth, MaxWidth));
        var moved = actors.Select(a => Math.Abs(a.At.Z - lane.Length) < 0.01f || a.At.Z > resized.Length ? a with { At = a.At with { Z = resized.Length } } : a).ToList();
        return (resized, moved);
    }

    private static JsonNode? ActorsJson(IReadOnlyList<ActorSpec> actors) => ChartJson.ToNode(actors.Select(actor => new ActorDto
    {
        Id = actor.Id,
        Name = actor.Name,
        At = new PointDto { X = Round(actor.At.X), Z = Round(actor.At.Z), Facing = actor.At.Facing == 0f ? null : Round(actor.At.Facing) },
        Look = actor.Look,
        Shiftstones = actor.Shiftstones == null ? null : JsonSerializer.SerializeToElement(actor.Shiftstones),
        Measurement = actor.Measurement is { } size ? new MeasurementDto { Height = MathF.Round(size.Height, 3), ArmSpan = MathF.Round(size.ArmSpan, 3) } : null,
    }).ToList());

    // Every number called `name` anywhere inside `node`, scaled and kept from `least` to `most`.
    private static void ScaleNumbersNamed(string name, JsonNode? node, float factor, double least, double most)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var item in array) ScaleNumbersNamed(name, item, factor, least, most);
                break;

            case JsonObject properties:
                foreach ((var key, var value) in properties.ToList())
                {
                    if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase) && value is JsonValue number && number.TryGetValue<double>(out var before))
                        properties[key] = Math.Round(Math.Clamp(before * factor, least, most), 2);
                    else
                        ScaleNumbersNamed(name, value, factor, least, most);
                }

                break;
        }
    }

    private static float Round(float value) => MathF.Round(value, 2);
}
