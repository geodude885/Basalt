using Il2CppRUMBLE.Combat.ShiftStones;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;

namespace BossBattles.Actors;

internal static class Shiftstones
{
    private static List<(string Pool, string Display)>? _catalog;

    public static IReadOnlyList<(string Pool, string Display)> Catalog()
    {
        if (_catalog != null) return _catalog;

        List<(string Pool, string Display)> found;
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
        Logger.Debug($"[Actor.StoneCatalog] {string.Join(", ", found.Select(f => $"{f.Pool} ({f.Display})"))}");
        return _catalog;
    }

    /// <summary>The pool for a chart's stone name, or null.</summary>
    public static string? Pool(string name)
    {
        var wanted = StringHelpers.Squash(name);
        var names = new[] { wanted, wanted + "stone" };
        return Catalog().FirstOrDefault(s => names.Contains(StringHelpers.Squash(s.Pool)) || names.Contains(StringHelpers.Squash(s.Display))).Pool;
    }

    /// <summary>The stones a player has equipped, by pool name.</summary>
    public static List<string> EquippedBy(PlayerController player)
    {
        var names = player.PlayerShiftstones.GetEquippedShiftstones()
            .Where(stone => stone != null)
            .Select(stone => stone.gameObject.name.Replace("(Clone)", "", StringComparison.Ordinal).Trim())
            .ToList();

        return names;
    }
}
