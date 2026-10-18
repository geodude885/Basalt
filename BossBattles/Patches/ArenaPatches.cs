using BossBattles.Battle;
using HarmonyLib;
using Il2CppRUMBLE.Networking.MatchFlow;

namespace BossBattles.Patches;

/// <summary>
/// The match handler force-quits a solo player from the Ring or Pit back to the Gym; blocked only
/// while a battle holds the arena, so real matches and MultiplayerMapsInSinglePlayer are unaffected.
/// </summary>
public static class ArenaPatches
{
    private static BattleController _battle = null!;

    internal static int Apply(HarmonyLib.Harmony harmony, BattleController battle)
    {
        _battle = battle;
        return PatchGroup.Apply(harmony, typeof(ArenaPatches));
    }

    [HarmonyPatch(typeof(MatchHandler), nameof(MatchHandler.ForceQuit))]
    public static class NoForceQuitDuringBattle
    {
        public static bool Prefix()
        {
            var runOriginal = true;

            PatchGuard.Run(nameof(NoForceQuitDuringBattle), () =>
            {
                if (!_battle.StayInMap) return;

                runOriginal = false;
                Logger.Debug("[Arena.ForceQuitBlocked] the match handler tried to send you back to the Gym mid-battle");
            });

            return runOriginal;
        }
    }
}
