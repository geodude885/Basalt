using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BossBattles.Services.Charts;

/// <summary>What lane setup saves: the chart's <c>arena</c>, <c>lane</c> and <c>actors</c>, written in place.</summary>
public static class ChartSetup
{
    public const float MinLength = 3f;
    public const float MaxLength = 40f;
    public const float MinWidth = 1f;
    public const float MaxWidth = 8f;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions Indented = new(Options) { WriteIndented = true };

    /// <summary>The chart text with its arena, lane and actors set.</summary>
    public static string Write(string chartText, ArenaSpec arena, Lane lane, IReadOnlyList<ActorSpec> actors)
    {
        var text = ChartDocument.SetRaw(chartText, "arena", JsonSerializer.Serialize(Arena(arena), Options));
        text = ChartDocument.SetRaw(text, "lane", JsonSerializer.Serialize(new LaneDto(Round(lane.Length), Round(lane.Width)), Options));
        return ChartDocument.SetRaw(text, "actors", JsonSerializer.Serialize(actors.Select(Actor).ToList(), Indented));
    }

    /// <summary>
    /// The chart text with every <c>"z"</c> in <c>events</c> and <c>phases</c> scaled by
    /// <paramref name="factor"/>, so it keeps its shape on a resized lane. Changed where they stand.
    /// </summary>
    public static string ScaleDepth(string chartText, float factor, float length)
    {
        if (Math.Abs(factor - 1f) < 0.001f) return chartText;

        var text = chartText;
        foreach (var list in new[] { "events", "phases" })
        {
            if (!ChartDocument.TryFindValue(text, list, out var start, out var end)) continue;

            var scaled = DepthNumber.Replace(text[start..end], m =>
            {
                var z = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                // Projectiles may start past the far end; actor spots stay in the lane (and scale with it).
                double most = list == "events" ? length + ChartLoader.ProjectileReach : length;
                return m.Groups[1].Value + Format(Math.Round(Math.Clamp(z * factor, 0, most), 2));
            });
            text = text[..start] + scaled + text[end..];
        }

        // Your zone's length goes with the lane too.
        if (ChartDocument.TryFindValue(text, "zone", out var zoneStart, out var zoneEnd))
        {
            var zone = ZoneLength.Replace(text[zoneStart..zoneEnd], m =>
            {
                var z = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                return m.Groups[1].Value + Format(Math.Round(Math.Clamp(z * factor, 0.5, length), 2));
            });
            text = text[..zoneStart] + zone + text[zoneEnd..];
        }

        return text;
    }

    /// <summary>A <c>"length": number</c> pair.</summary>
    private static readonly System.Text.RegularExpressions.Regex ZoneLength =
        new(@"(""length""\s*:\s*)(-?\d+(?:\.\d+)?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>A <c>"z": number</c> pair.</summary>
    private static readonly System.Text.RegularExpressions.Regex DepthNumber =
        new(@"(""z""\s*:\s*)(-?\d+(?:\.\d+)?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>A lane resized within limits; actors at the old far end, or past the new one, move to the new far end.</summary>
    public static (Lane Lane, IReadOnlyList<ActorSpec> Actors) Resize(Lane lane, IReadOnlyList<ActorSpec> actors, float length, float width)
    {
        var resized = new Lane(Math.Clamp(Round(length), MinLength, MaxLength), Math.Clamp(Round(width), MinWidth, MaxWidth));
        var moved = actors.Select(a => Math.Abs(a.At.Z - lane.Length) < 0.01f || a.At.Z > resized.Length ? a with { At = a.At with { Z = resized.Length } } : a).ToList();
        return (resized, moved);
    }

    private static ArenaDto Arena(ArenaSpec arena) => new(
        arena.Map.ToString().ToLowerInvariant(),
        arena.HasSpot ? new PointDto(Round(arena.X!.Value), Round(arena.Z!.Value), null) : null,
        arena.Facing is { } f ? Round(f) : null);

    private static ActorDto Actor(ActorSpec actor) => new(
        actor.Id,
        actor.Name,
        new PointDto(Round(actor.At.X), Round(actor.At.Z), actor.At.Facing == 0f ? null : Round(actor.At.Facing)),
        actor.Look,
        actor.Shiftstones);

    private static float Round(float value) => MathF.Round(value, 2);

    private sealed record LaneDto(float Length, float Width);

    private sealed record ArenaDto(string Map, PointDto? At, float? Facing);

    private sealed record PointDto(float X, float Z, float? Facing);

    private sealed record ActorDto(string Id, string Name, PointDto At, string Look, IReadOnlyList<string>? Shiftstones);

    /// <summary>A number as a chart writes it.</summary>
    public static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
