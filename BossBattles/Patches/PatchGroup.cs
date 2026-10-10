using BossBattles.Core;

namespace BossBattles.Patches;

internal static class PatchGroup
{
    /// <summary>
    /// Patches each nested class of a group on its own (<c>PatchAll</c> on the container patches
    /// nothing), logging before each so a native crash while patching names the patch.
    /// </summary>
    public static int Apply(HarmonyLib.Harmony harmony, Type group)
    {
        var total = 0;

        foreach (Type patch in group.GetNestedTypes())
        {
            Mod.Log?.Line($"patching {group.Name}.{patch.Name}...");
            try
            {
                var count = harmony.CreateClassProcessor(patch).Patch()?.Count ?? 0;
                total += count;
                Mod.Note(count == 0 ? "Patches.PatchedNothing" : "Patches.Patched", $"{group.Name}.{patch.Name}", echo: count == 0);
            }
            catch (Exception e)
            {
                Mod.Note("Patches.PatchFailed", $"{group.Name}.{patch.Name}: {e.Message}");
            }
        }

        return total;
    }
}
