namespace Basalt.Services.Modes;

public enum GameMode
{
    Normal,
    Practice,
}

public static class GameModes
{
    public static IReadOnlyList<GameMode> All { get; } = Enum.GetValues<GameMode>();

    public static bool SavesScores(this GameMode mode) => mode == GameMode.Normal;

    public static bool PlayerInvincible(this GameMode mode) => mode == GameMode.Practice;

    public static string Describe(this GameMode mode) => mode switch
    {
        GameMode.Practice => "Play whilst invincible, scores are not saved",
        _ => "Play as normal",
    };
}
