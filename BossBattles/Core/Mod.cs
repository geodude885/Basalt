using BossBattles.Actors;
using BossBattles.Battle;
using BossBattles.Services.Logging;
using MelonLoader;
using UnityEngine;

namespace BossBattles.Core;

/// <summary>
/// State the whole mod shares. Observations go through <see cref="Note"/>: counted in the census and
/// written through to <c>battles.log</c>, so a run that ends in a crash still says what happened.
/// </summary>
internal static class Mod
{
    /// <summary>Past this many lines a counter is only counted: some fire every physics step.</summary>
    private const int MaxLinesPerCounter = 30;

    public static Census Census { get; } = new();

    public static ModLog? Log { get; set; }

    public static MelonLogger.Instance? Melon { get; set; }

    public static ActorRegistry Actors { get; } = new();

    public static BattleController Battle { get; } = new();

    public static Studio Studio { get; } = new();

    /// <summary>The camera mouse clicks come through, when not the main one (the dev tools' desktop camera).</summary>
    public static Func<Camera?>? ClickCamera { get; set; }

    /// <param name="counter">Census name, one per kind of observation.</param>
    /// <param name="detail">The log line.</param>
    /// <param name="echo">Also print to the MelonLoader console; off for anything frequent.</param>
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

    /// <summary>A line that is never capped or counted.</summary>
    public static void Say(string line)
    {
        Log?.Line(line);
        Melon?.Msg(line);
    }

    public static void Report(string reason) => Say($"census ({reason}): {Census.Report()}");
}
