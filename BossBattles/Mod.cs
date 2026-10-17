using BossBattles.Actors;
using BossBattles.Battle;
using BossBattles.Editing;

namespace BossBattles;

// State the whole mod shares
internal static class Mod
{
    public static ActorRegistry Actors { get; } = new();

    public static BattleController Battle { get; } = new();

    public static Studio Studio { get; } = new();
}
