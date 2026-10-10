using System.Globalization;

namespace BossBattles.Services.Charts;

/// <summary>A colour from a chart: red, green, blue and opacity, each 0 to 1.</summary>
public readonly record struct Rgba(float R, float G, float B, float A = 1f)
{
    /// <summary>Nothing at all - "none", which hides walls a chart shows.</summary>
    public static Rgba None => new(0f, 0f, 0f, 0f);

    /// <summary>"#RRGGBB" or "#RRGGBBAA" (the # optional), as a colour picker writes it, or "none".</summary>
    public static bool TryParse(string? text, out Rgba colour)
    {
        colour = default;
        if (string.Equals(text?.Trim(), "none", StringComparison.OrdinalIgnoreCase))
        {
            colour = None;
            return true;
        }

        var hex = (text ?? "").Trim().TrimStart('#');
        if ((hex.Length != 6 && hex.Length != 8) || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return false;

        if (hex.Length == 6) value = (value << 8) | 0xFF;
        colour = new Rgba(((value >> 24) & 0xFF) / 255f, ((value >> 16) & 0xFF) / 255f, ((value >> 8) & 0xFF) / 255f, (value & 0xFF) / 255f);
        return true;
    }
}

/// <summary>
/// How a chart's lane looks: the lines on the floor, the backstop's frame and - when given - the
/// invisible walls, drawn as see-through panes on their inner faces. Null keeps the mod's own colour
/// (and the walls unseen).
/// </summary>
public sealed record LaneColours(Rgba? Edges = null, Rgba? Ends = null, Rgba? Zone = null, Rgba? Backstop = null, Rgba? Walls = null)
{
    public static LaneColours Default { get; } = new();

    /// <summary>These colours laid over <paramref name="under"/>: anything not set here is taken from it.</summary>
    public LaneColours Over(LaneColours under) =>
        new(Edges ?? under.Edges, Ends ?? under.Ends, Zone ?? under.Zone, Backstop ?? under.Backstop, Walls ?? under.Walls);

    /// <summary>Whether the walls are shown - a wall colour that is not "none".</summary>
    public bool ShowsWalls => Walls is { A: > 0f };
}
