namespace Basalt.Services.Charts.Model;

public readonly record struct PlayerZone(float Length, float Height)
{
    public const float DefaultClearance = 2f;
    public const float DefaultHeight = 3.5f;

    internal static PlayerZone From(ChartDto dto, Lane lane)
    {
        var defaults = For(lane);
        return dto.PlayerZone is { } zone ? new PlayerZone(zone.Length ?? defaults.Length, zone.Height ?? defaults.Height) : defaults;
    }

    public static PlayerZone For(Lane lane) =>
        new(lane.Length > DefaultClearance * 2 ? lane.Length - DefaultClearance : lane.Length / 2f, DefaultHeight);
}
