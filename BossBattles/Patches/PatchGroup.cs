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

        foreach (var patch in group.GetNestedTypes())
        {
            Logger.Debug($"patching {group.Name}.{patch.Name}...");
            try
            {
                var count = harmony.CreateClassProcessor(patch).Patch()?.Count ?? 0;
                total += count;
                Logger.Write(count == 0 ? LogLevel.Warning : LogLevel.Debug, $"[{(count == 0 ? "Patches.PatchedNothing" : "Patches.Patched")}] {group.Name}.{patch.Name}");
            }
            catch (Exception e)
            {
                Logger.Error($"[Patches.PatchFailed] {group.Name}.{patch.Name}: {e.Message}");
            }
        }

        return total;
    }
}
