namespace Basalt.Services.Charts.Model;

public record Rules(SpawnRule PlayerSpawns)
{
    public const double DefaultBreakStillAfterSeconds = 3;

    public static Rules Default { get; } = new(SpawnRule.All);
    public double BreakStillAfterSeconds { get; init; } = DefaultBreakStillAfterSeconds;
    public bool BreakBehind { get; init; }
}

public enum SpawnRule
{
    All,
    Discs,
    None
}
