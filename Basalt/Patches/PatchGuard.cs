namespace Basalt.Patches;

internal static class PatchGuard
{
    public static bool Enabled { get; set; } = true;

    // If any patches fail, don't crash, just disable all patches
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
            Logger.Error($"[Patches.Disabled] {name} threw, all patch bodies off for this session: {e}");
        }
    }
}
