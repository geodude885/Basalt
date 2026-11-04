using System.Numerics;

namespace Basalt.Services.Charts.Model;

public abstract record ChartEvent(double Time, int Order, string ActorId) // Order is tie breaker for same time events
{
    internal static List<ChartEvent> AllFrom(ChartDto dto, TempoMap? tempo, string defaultActorId)
        => (dto.Events ?? []).Select((e, ord) => From(e, ord, tempo, defaultActorId)).ToList();

    private static ChartEvent From(EventDto e, int order, TempoMap? tempo, string defaultActorId)
    {
        var time = tempo is { } && e.Beat is { } beats ? tempo.BeatToSeconds(beats) : e.T ?? 0;
        var actor = e.Actor ?? defaultActorId;

        return e switch
        {
            { Projectile: { } projectile } => ProjectileEvent.FromDto(projectile, time, order, actor),
            { Move: { } move } => new MoveEvent(time, order, actor, move),
            _ => new PlaceEvent(time, order, actor, new(e.At!.X!.Value, e.At!.Z!.Value, e.At!.Facing ?? 0f)),
        };
    }

}


public record ProjectileEvent(
    double Time,
    int Order,
    string Structure,
    Vector3 From,
    Aim To,
    float Speed,
    float Telegraph,
    string ActorId) : ChartEvent(Time, Order, ActorId)
{
    public const float DefaultTelegraph = 0.5f;

    public bool? Grounded { get; init; }

    public static bool GroundedByDefault(string structure) =>
        structure.Equals("Pillar", StringComparison.OrdinalIgnoreCase)
        || structure.Equals("RockCube", StringComparison.OrdinalIgnoreCase)
        || structure.Equals("Wall", StringComparison.OrdinalIgnoreCase);

    public bool IsGrounded => Grounded ?? GroundedByDefault(Structure);

    public bool SpawnsFromFloor => FloorSpawned.Contains(Structure, StringComparer.OrdinalIgnoreCase);
    public bool RestsOnFloor => SpawnsFromFloor || IsGrounded;
    private static readonly string[] FloorSpawned = ["Disc", "Pillar", "RockCube", "Wall"];

    internal static ProjectileEvent FromDto(ProjectileDto projectile, double time, int order, string actor)
    {
        var from = new Vector3(projectile.From!.X!.Value, projectile.From.Y!.Value, projectile.From.Z!.Value);
        var to = projectile.To == null
            ? Aim.PlayerOffset(projectile.Offset?.X ?? 0f, projectile.Offset?.Y ?? 0f)
            : Aim.Point(projectile.To.X!.Value, projectile.To.Y!.Value);
        var telegraph = projectile.Telegraph ?? DefaultTelegraph;

        return new ProjectileEvent(time, order, projectile.Structure!, from, to, projectile.Speed!.Value, telegraph, actor) { Grounded = projectile.Grounded };
    }
}

public readonly record struct Aim(bool AtPlayer, float X, float Y)
{
    public static Aim Player => new(true, 0f, 0f);
    public static Aim PlayerOffset(float right, float up) => new(true, right, up);
    public static Aim Point(float x, float y) => new(false, x, y);
}

public record MoveEvent(double Time, int Order, string ActorId, string Move) : ChartEvent(Time, Order, ActorId);

public record PlaceEvent(double Time, int Order, string ActorId, Spot At) : ChartEvent(Time, Order, ActorId);
