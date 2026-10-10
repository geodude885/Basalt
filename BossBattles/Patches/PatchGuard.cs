using BossBattles.Core;

namespace BossBattles.Patches;

/// <summary>
/// Runs patch bodies so none can throw into the game: the first failure is logged and turns every
/// patch body off for the session, leaving the game's own behaviour.
/// </summary>
internal static class PatchGuard
{
    public static bool Enabled { get; set; } = true;

    public static void Run(string name, Action body)
    {
        if (!Enabled) return;

        try
        {
            body();
        }
        catch (Exception e)
        {
            Enabled = false;
            Mod.Note("Patches.Disabled", $"{name} threw, all patch bodies off for this session: {e}");
        }
    }
}
