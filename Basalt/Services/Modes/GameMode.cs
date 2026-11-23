namespace Basalt.Services.Modes;

public enum GameMode
{
    Normal,
    Practice,
    Hardcore,
}

public static class GameModes
{
    public const int DamagePerHit = 1;

    public static IReadOnlyList<GameMode> All { get; } = Enum.GetValues<GameMode>();

    public static bool SavesScores(this GameMode mode) => mode != GameMode.Practice;

    public static bool PlayerInvincible(this GameMode mode) => mode == GameMode.Practice;

    public static bool FullStructureDamage(this GameMode mode) => mode == GameMode.Hardcore;

    public static string Describe(this GameMode mode) => mode switch
    {
        GameMode.Practice => "Practice whilst invincible. Scores are not saved",
        GameMode.Hardcore => "Structures do their full damage. Just for fun, doesn't have any extra rewards",
        _ => "Play as normal. All structures do 1 damage",
    };
}
