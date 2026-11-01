
namespace Basalt.Services.Charts.Model;

public record LaneColours(Rgba? SideLines = null, Rgba? EndLines = null, Rgba? ZoneLine = null, Rgba? Backstop = null, Rgba? Walls = null)
{
    public static LaneColours Default { get; } = new();

    public LaneColours Over(LaneColours under) =>
        new(SideLines ?? under.SideLines, EndLines ?? under.EndLines, ZoneLine ?? under.ZoneLine, Backstop ?? under.Backstop, Walls ?? under.Walls);

    public bool ShowsWalls => Walls is { A: > 0f };

    internal static LaneColours From(ChartDto? dto)
    {
        if (dto?.Colours is not { } colours) return Default;
        return From(colours);
    }

    internal static LaneColours From(ColoursDto? colours) => colours == null
        ? Default
        : new(
            ReadColour(colours.SideLines),
            ReadColour(colours.EndLines),
            ReadColour(colours.ZoneLine),
            ReadColour(colours.Backstop),
            ReadColour(colours.Walls));

    private static Rgba? ReadColour(string? text) => text != null && Rgba.TryParse(text, out var colour) ? colour : null;
}
