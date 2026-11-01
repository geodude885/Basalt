namespace Basalt.Services.Charts.Model;

public record ArenaSpec(ArenaKind Map, float X = 0f, float Z = 0f, float Facing = 0f)
{
    internal static ArenaSpec From(ChartDto dto)
    {
        if (dto.Arena is not { } arena) return Default;

        var map = arena.Map == null ? Default.Map : Enum.Parse<ArenaKind>(arena.Map, ignoreCase: true);
        return new ArenaSpec(map, arena.At?.X ?? 0f, arena.At?.Z ?? 0f, arena.At?.Facing ?? 0f);
    }

    public static ArenaSpec Default { get; } = new(ArenaKind.Ring, 0f, -6f, 0f);
}

public enum ArenaKind
{
    Gym,
    Ring,
    Pit
}
