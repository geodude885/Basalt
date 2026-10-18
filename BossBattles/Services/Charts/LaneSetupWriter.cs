using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BossBattles.Services.Charts;

// What lane setup saves into a chart: its arena, lane and actors, written in place.
public static class LaneSetupWriter
{
    public const float MinLength = 3f;
    public const float MaxLength = 40f;
    public const float MinWidth = 1f;
    public const float MaxWidth = 8f;

    private static readonly Regex DepthNumber = new(@"(""z""\s*:\s*)(-?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
    private static readonly Regex LengthNumber = new(@"(""length""\s*:\s*)(-?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);

    public static string Write(string chartText, ArenaSpec arena, Lane lane, IReadOnlyList<ActorSpec> actors)
    {
        var arenaJson = new ArenaDto
        {
            Map = arena.Map.ToString().ToLowerInvariant(),
            At = arena.HasSpot ? new PointDto { X = Round(arena.X!.Value), Z = Round(arena.Z!.Value) } : null,
            Facing = arena.Facing is { } facing ? Round(facing) : null,
        };
        var text = ChartText.Set(chartText, "arena", JsonSerializer.Serialize(arenaJson, ChartJson.Options));
        text = ChartText.Set(text, "lane", JsonSerializer.Serialize(new LaneDto { Length = Round(lane.Length), Width = Round(lane.Width) }, ChartJson.Options));
        return WriteActors(text, actors);
    }

    public static string WriteActors(string chartText, IReadOnlyList<ActorSpec> actors)
    {
        var actorsJson = actors.Select(actor => new ActorDto
        {
            Id = actor.Id,
            Name = actor.Name,
            At = new PointDto { X = Round(actor.At.X), Z = Round(actor.At.Z), Facing = actor.At.Facing == 0f ? null : Round(actor.At.Facing) },
            Look = actor.Look,
            Shiftstones = actor.Shiftstones == null ? null : JsonSerializer.SerializeToElement(actor.Shiftstones),
            Measurement = actor.Measurement is { } size ? new MeasurementDto { Height = MathF.Round(size.Height, 3), ArmSpan = MathF.Round(size.ArmSpan, 3) } : null,
        }).ToList();
        return ChartText.Set(chartText, "actors", JsonSerializer.Serialize(actorsJson, ChartJson.Indented));
    }

    // Every "z" in the events and phases, and the zone's length, scaled by `factor`, so the chart keeps its shape on a
    // resized lane. Changed where they stand: everything else in the text is left as written.
    public static string ScaleDepth(string chartText, float factor, float length)
    {
        if (Math.Abs(factor - 1f) < 0.001f) return chartText;

        // Projectiles may start past the far end; actor spots stay in the lane, and so does your zone.
        var text = ChartText.EditValue(chartText, "events", events => ScaleNumbers(events, DepthNumber, factor, 0, length + ChartLoader.ProjectileReach));
        text = ChartText.EditValue(text, "phases", phases => ScaleNumbers(phases, DepthNumber, factor, 0, length));
        return ChartText.EditValue(text, "zone", zone => ScaleNumbers(zone, LengthNumber, factor, 0.5, length));
    }

    // A lane resized within limits; actors at the old far end, or past the new one, move to the new far end.
    public static (Lane Lane, IReadOnlyList<ActorSpec> Actors) Resize(Lane lane, IReadOnlyList<ActorSpec> actors, float length, float width)
    {
        var resized = new Lane(Math.Clamp(Round(length), MinLength, MaxLength), Math.Clamp(Round(width), MinWidth, MaxWidth));
        var moved = actors.Select(a => Math.Abs(a.At.Z - lane.Length) < 0.01f || a.At.Z > resized.Length ? a with { At = a.At with { Z = resized.Length } } : a).ToList();
        return (resized, moved);
    }

    // A number as a chart writes it.
    public static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string ScaleNumbers(string json, Regex numbers, float factor, double least, double most) =>
        numbers.Replace(json, match =>
        {
            var number = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            return match.Groups[1].Value + Format(Math.Round(Math.Clamp(number * factor, least, most), 2));
        });

    private static float Round(float value) => MathF.Round(value, 2);
}
