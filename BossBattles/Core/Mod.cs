using BossBattles.Actors;
using BossBattles.Battle;
using BossBattles.Services.Logging;
using MelonLoader;

namespace BossBattles.Core;

/// <summary>
/// What the whole mod shares: the log, the census of what happened, the actors in play, and the
/// battle. Every observation goes through <see cref="Note"/>: counted by name, written through to
/// <c>UserData/BossBattles/battles.log</c> (so a run that ends in a crash still says what happened),
/// and optionally echoed to the MelonLoader console.
/// </summary>
internal static class Mod
{
    /// <summary>Past this many lines a counter is only counted - a structure resting on an actor fires every physics step.</summary>
    private const int MaxLinesPerCounter = 30;

    public static Census Census { get; } = new();

    public static ModLog? Log { get; set; }

    public static MelonLogger.Instance? Melon { get; set; }

    public static ActorRegistry Actors { get; } = new();

    public static BattleController Battle { get; } = new();

    /// <summary>Recording takes into charts (chart editing).</summary>
    public static Studio Studio { get; } = new();

    /// <param name="counter">Census name; one per kind of observation.</param>
    /// <param name="detail">What was seen, for the log line.</param>
    /// <param name="echo">Also print to the MelonLoader console. Off for anything frequent.</param>
    public static void Note(string counter, string detail, bool echo = true)
    {
        Census.Count(counter);

        var seen = Census[counter];
        if (seen > MaxLinesPerCounter) return;

        var line = seen == MaxLinesPerCounter
            ? $"[{counter}] {detail} (further lines suppressed, still counted in the census)"
            : $"[{counter}] {detail}";
        Log?.Line(line);
        if (echo) Melon?.Msg(line);
    }

    /// <summary>A line that is never capped or counted - battle results, self-test verdicts.</summary>
    public static void Say(string line)
    {
        Log?.Line(line);
        Melon?.Msg(line);
    }

    public static void Report(string reason) => Say($"census ({reason}): {Census.Report()}");
}
