using Il2CppRUMBLE.Combat.ShiftStones;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;

namespace Basalt.Actors;

internal static class Shiftstones
{
    private static List<(string Id, string Display)>? _catalog;

    // Try parse any name to loaded shiftstone pool name
    public static string? ToIdOrDefault(string name)
    {
        var wanted = name.Squash();
        var names = new[] { wanted, wanted + "stone" };
        return Catalog().FirstOrDefault(s => names.Contains(s.Id.Squash()) || names.Contains(s.Display.Squash())).Id;
    }

    // A player's equipped shiftstone ids
    public static List<string> EquippedBy(PlayerController player)
        => player.PlayerShiftstones.GetEquippedShiftstones()
            .Where(stone => stone != null)
            .Select(stone => stone.gameObject.name.Replace("(Clone)", "", StringComparison.Ordinal).Trim())
            .ToList();

    // All loaded shiftstones
    private static IReadOnlyList<(string Id, string Display)> Catalog()
    {
        if (_catalog != null) return _catalog;

        List<(string Id, string Display)> found;
        try
        {
            found = PoolManager.instance.resourcesToPool.ToArray()
                .Select(settings => (Settings: settings, Stone: settings?.Resource?.GetComponent<ShiftStone>()))
                .Where(s => s.Stone != null)
                .Select(s => (s.Settings!.Resource.gameObject.name, s.Stone!.StoneName ?? ""))
                .ToList();
        }
        catch (Exception e)
        {
            Logger.Error($"[Actor.StoneCatalogFailed] {e.Message}");
            return Array.Empty<(string, string)>();
        }

        _catalog = found;
        Logger.Debug($"[Actor.StoneCatalog] {string.Join(", ", found.Select(f => $"{f.Id} ({f.Display})"))}");
        return _catalog;
    }
}
