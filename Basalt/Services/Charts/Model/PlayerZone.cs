namespace Basalt.Services.Charts.Model;

public readonly record struct PlayerZone(float Length, float Height)
{
    public const float DefaultClearance = 2f;
    public const float DefaultHeight = 3.5f;

    public static PlayerZone For(Lane lane) =>
        new(lane.Length > DefaultClearance * 2 ? lane.Length - DefaultClearance : lane.Length / 2f, DefaultHeight);
}
