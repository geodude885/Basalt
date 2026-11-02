using Basalt.Battle;
using HarmonyLib;
using Il2CppRUMBLE.Networking.MatchFlow;

namespace Basalt.Patches;

/// <summary>
/// The match handler force-quits a solo player from the Ring or Pit back to the Gym; blocked only
/// in an arena a chart brought you to, so real matches and MultiplayerMapsInSinglePlayer are unaffected.
/// It sets its match error first, and the Gym would show that as a slab on your way back.
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
                if (!_battle.InChartArena) return;

                runOriginal = false;
                MatchHandler.CurrentMatchError = MatchHandler.MatchError.None;
                Logger.Debug("[Arena.ForceQuitBlocked] the match handler tried to send you back to the Gym from a chart's arena");
            });

            return runOriginal;
        }
    }
}
