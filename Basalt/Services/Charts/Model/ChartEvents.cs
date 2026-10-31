using System.Numerics;

namespace Basalt.Services.Charts.Model;

public abstract record ChartEvent(double Time, int Order); // Order is tie breaker for same time events

public record ProjectileEvent(
    double Time,
    int Order,
    string Structure,
    Vector3 From,
    Aim To,
    float Speed,
    float Telegraph,
    string ActorId) : ChartEvent(Time, Order)
{
    public bool? Grounded { get; init; }

    public static bool GroundedByDefault(string structure) =>
        structure.Equals("Pillar", StringComparison.OrdinalIgnoreCase)
        || structure.Equals("RockCube", StringComparison.OrdinalIgnoreCase)
        || structure.Equals("Wall", StringComparison.OrdinalIgnoreCase);

    public bool IsGrounded => Grounded ?? GroundedByDefault(Structure);

    public bool SpawnsFromFloor => FloorSpawned.Contains(Structure, StringComparer.OrdinalIgnoreCase);
    public bool RestsOnFloor => SpawnsFromFloor || IsGrounded;
    private static readonly string[] FloorSpawned = ["Disc", "Pillar", "RockCube", "Wall"];
}

public readonly record struct Aim(bool AtPlayer, float X, float Y)
{
    public static Aim Player => new(true, 0f, 0f);
    public static Aim Point(float x, float y) => new(false, x, y);
}

public record MoveEvent(double Time, int Order, string ActorId, string Move) : ChartEvent(Time, Order);

public record PlaceEvent(double Time, int Order, string ActorId, Spot At) : ChartEvent(Time, Order);
