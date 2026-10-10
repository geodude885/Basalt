using BossBattles.Core;

namespace BossBattles.Patches;

/// <summary>
/// No patch body may throw - a throw surfaces as the game's own method failing, every frame. Each
/// body runs through here: the first failure is logged and switches every patch body off for the
/// session, leaving the game's own behaviour.
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
