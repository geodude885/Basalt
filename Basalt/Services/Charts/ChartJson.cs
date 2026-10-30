using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Basalt.Services.Charts;

// A chart file's JSON, as ChartReader reads it and the writers change it.
internal static class ChartJson
{

    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // The chart as an object to change; setting "tracks" replaces a "Tracks" already there.
    public static JsonObject Parse(string chartText)
    {
        try
        {
            return JsonNode.Parse(chartText, new JsonNodeOptions { PropertyNameCaseInsensitive = true }, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                   ?? throw new FormatException("a chart is a JSON object: it must start with {");
        }
        catch (JsonException e)
        {
            throw new FormatException($"not valid chart JSON: {e.Message}", e);
        }
    }

    public static JsonNode? ToNode<T>(T value) => JsonSerializer.SerializeToNode(value, Options);

    public static string Write(JsonObject chart) => chart.ToJsonString(Options);
}

internal class ChartDto
{
    public int? Format { get; set; }
    public string? Name { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string? Difficulty { get; set; }
    public ArenaDto? Arena { get; set; }
    public MusicDto? Music { get; set; }
    public string? Cover { get; set; }
    public List<TrackDto>? Tracks { get; set; }
    public ZoneDto? Zone { get; set; }
    public RulesDto? Rules { get; set; }
    public LaneDto? Lane { get; set; }
    public JsonElement? Bpm { get; set; }
    public List<ActorDto>? Actors { get; set; }
    public List<PhaseDto>? Phases { get; set; }
    public List<EventDto>? Events { get; set; }
    public List<VisualDto>? Visuals { get; set; }
    public ColoursDto? Colours { get; set; }
    public ColoursDto? Colors { get; set; }
    public double? End { get; set; }
}

internal class ColoursDto
{
    public string? Edges { get; set; }
    public string? Ends { get; set; }
    public string? Zone { get; set; }
    public string? Backstop { get; set; }
    public string? Walls { get; set; }
}

internal class VisualDto
{
    public double? T { get; set; }
    public double? Beat { get; set; }
    public string? Pack { get; set; }
    public Dictionary<string, float>? Set { get; set; }
    public bool? On { get; set; }
    public bool? Off { get; set; }
    public double? Fade { get; set; }
}

internal class ZoneDto
{
    public float? Length { get; set; }
    public float? Height { get; set; }
}

internal class RulesDto
{
    public string? Spawn { get; set; }
    public double? Cleanup { get; set; }
    public bool? BreakBehind { get; set; }
}

internal class TrackDto
{
    public string? Actor { get; set; }
    public List<TrackClipDto>? Clips { get; set; }
}

internal class TrackClipDto
{
    public double? T { get; set; }
    public double? Beat { get; set; }
    public string? Clip { get; set; }
    public double? From { get; set; }
    public double? To { get; set; }
    public bool? Mime { get; set; }
}

internal class ArenaDto
{
    public string? Map { get; set; }
    public PointDto? At { get; set; }
    public float? Facing { get; set; }
}

internal class MusicDto
{
    public string? File { get; set; }
    public double? Start { get; set; }
    public float? Volume { get; set; }
}

internal class LaneDto
{
    public float? Length { get; set; }
    public float? Width { get; set; }
}

internal class ActorDto
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public PointDto? At { get; set; }
    public string? Look { get; set; }
    public JsonElement? Shiftstones { get; set; }
    public MeasurementDto? Measurement { get; set; }
}

internal class MeasurementDto
{
    public float? Height { get; set; }
    public float? ArmSpan { get; set; }
}

internal class PhaseDto
{
    public string? Name { get; set; }
    public double? Start { get; set; }
    public double? StartBeat { get; set; }
    public double? ScoreMultiplier { get; set; }
    public string? Actor { get; set; }
    public PointDto? At { get; set; }
    public ColoursDto? Colours { get; set; }
    public ColoursDto? Colors { get; set; }
}

internal class EventDto
{
    public double? T { get; set; }
    public double? Beat { get; set; }
    public string? Actor { get; set; }
    public ProjectileDto? Projectile { get; set; }
    public string? Move { get; set; }
    public PointDto? At { get; set; }
}

internal class ProjectileDto
{
    public string? Structure { get; set; }
    public PointDto? From { get; set; }
    public JsonElement? To { get; set; }
    public float? Speed { get; set; }
    public float? Telegraph { get; set; }
    public string? By { get; set; }
    public bool? Grounded { get; set; }
}

internal class PointDto
{
    public float? X { get; set; }
    public float? Y { get; set; }
    public float? Z { get; set; }
    public float? Facing { get; set; }
}
