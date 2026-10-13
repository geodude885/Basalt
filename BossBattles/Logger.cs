using MelonLoader;

namespace BossBattles;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

internal static class Logger
{
    public static LogLevel Level { get; set; } = LogLevel.Info;

    public static MelonLogger.Instance? Melon { get; set; }

    public static event Action<LogLevel, string>? Written;

    public static void Debug(string message) => Write(LogLevel.Debug, message);

    public static void Info(string message) => Write(LogLevel.Info, message);

    public static void Warning(string message) => Write(LogLevel.Warning, message);

    public static void Error(string message) => Write(LogLevel.Error, message);

    public static void Write(LogLevel level, string message)
    {
        Written?.Invoke(level, message);
        if (level < Level || Melon == null) return;

        switch (level)
        {
            case LogLevel.Error:
                Melon.Error(message);
                break;
            case LogLevel.Warning:
                Melon.Warning(message);
                break;
            default:
                Melon.Msg(message);
                break;
        }
    }
}
