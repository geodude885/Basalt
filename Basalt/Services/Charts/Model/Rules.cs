namespace Basalt.Services.Charts.Model;

public record Rules(SpawnRule PlayerSpawns)
{
    public const double DefaultBreakStillAfterSeconds = 3;
    public static Rules Default { get; } = new(SpawnRule.All);
    public double BreakStillAfterSeconds { get; init; } = DefaultBreakStillAfterSeconds;
    public bool BreakBehind { get; init; }

    internal static Rules From(ChartDto dto)
    {
        if (dto.Rules is not { } rules) return Default;

        var spawn = rules.PlayerSpawns == null ? SpawnRule.All : Enum.Parse<SpawnRule>(rules.PlayerSpawns, ignoreCase: true);

        return new Rules(spawn)
        {
            BreakStillAfterSeconds = rules.BreakStillAfter ?? DefaultBreakStillAfterSeconds,
            BreakBehind = rules.BreakBehind ?? false
        };
    }
}

public enum SpawnRule
{
    All,
    Discs,
    None
}
