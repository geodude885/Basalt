namespace Basalt.Services.Charts.Model;

public record ArenaSpec(ArenaKind Map, float X = 0f, float Z = 0f, float Facing = 0f)
{
    public static ArenaSpec Default { get; } = new(ArenaKind.Ring, 0f, -6f, 0f);
}

public enum ArenaKind
{
    Gym,
    Ring,
    Pit
}
