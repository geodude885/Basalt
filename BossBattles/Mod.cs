using BossBattles.Actors;
using BossBattles.Authoring;
using BossBattles.Battle;

namespace BossBattles;

/// <summary>State the whole mod shares.</summary>
internal static class Mod
{
    public static ActorRegistry Actors { get; } = new();

    public static BattleController Battle { get; } = new();

    public static Studio Studio { get; } = new();
}
