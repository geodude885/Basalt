using BossBattles.Actors;
using BossBattles.Battle;
using BossBattles.Editing;
using BossBattles.Services.Scoring;

namespace BossBattles;

// Built once at load. Kept static only for what cannot be handed anything: the Harmony patches and the DevTools.
internal static class Mod
{
    public static ActorRegistry Actors { get; private set; } = null!;

    public static BattleController Battle { get; private set; } = null!;

    public static Studio Studio { get; private set; } = null!;

    public static void Build(ScoreBook scores)
    {
        Actors = new ActorRegistry();
        Battle = new BattleController(Actors, scores);
        Studio = new Studio(Battle);
    }
}
