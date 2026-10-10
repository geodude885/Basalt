using BossBattles.Core;
using HarmonyLib;
using Il2CppRUMBLE.Networking.MatchFlow;

namespace BossBattles.Patches;

/// <summary>
/// Keeping you in the Ring or Pit with no opponent: the match handler force-quits a solo player
/// back to the Gym. MultiplayerMapsInSinglePlayer blocks that with the same prefix; this one only
/// acts while a battle holds the arena, so it is harmless alongside that mod and changes nothing
/// in real matches.
/// </summary>
public static class ArenaPatches
{
    public static int Apply(HarmonyLib.Harmony harmony) => PatchGroup.Apply(harmony, typeof(ArenaPatches));

    [HarmonyPatch(typeof(MatchHandler), nameof(MatchHandler.ForceQuit))]
    public static class NoForceQuitDuringBattle
    {
        public static bool Prefix()
        {
            var runOriginal = true;

            PatchGuard.Run(nameof(NoForceQuitDuringBattle), () =>
            {
                if (!Mod.Battle.HoldsArena) return;

                runOriginal = false;
                Mod.Note("Arena.ForceQuitBlocked", "the match handler tried to send you back to the Gym mid-battle", echo: false);
            });

            return runOriginal;
        }
    }
}
