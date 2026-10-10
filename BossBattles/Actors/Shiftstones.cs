using BossBattles.Core;
using Il2Cpp;
using Il2CppRUMBLE.Combat.ShiftStones;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;

namespace BossBattles.Actors;

/// <summary>
/// The game's shiftstones, read from the pool manager at runtime, so a chart can name one as
/// "Vigor", "VigorStone" or its display name.
/// </summary>
internal static class Shiftstones
{
    private static List<(string Pool, string Display)>? _catalog;

    public static IReadOnlyList<(string Pool, string Display)> Catalog()
    {
        if (_catalog != null) return _catalog;

        var found = new List<(string, string)>();
        try
        {
            foreach (PoolSettings? settings in PoolManager.instance.resourcesToPool)
            {
                ShiftStone? stone = settings?.Resource?.GetComponent<ShiftStone>();
                if (stone == null) continue;
                found.Add((settings!.Resource.gameObject.name, stone.StoneName ?? ""));
            }
        }
        catch (Exception e)
        {
            Mod.Note("Actor.StoneCatalogFailed", e.Message);
            return Array.Empty<(string, string)>();
        }

        _catalog = found;
        Mod.Note("Actor.StoneCatalog", string.Join(", ", found.Select(f => $"{f.Item1} ({f.Item2})")), echo: false);
        return _catalog;
    }

    /// <summary>The pool for a chart's stone name, or null.</summary>
    public static string? Pool(string name)
    {
        var wanted = Squash(name);
        foreach ((var pool, var display) in Catalog())
        {
            if (Squash(pool) == wanted || Squash(pool) == wanted + "stone" || Squash(display) == wanted || Squash(display) == wanted + "stone") return pool;
        }

        return null;
    }

    /// <summary>The stones a player has equipped, by pool name.</summary>
    public static List<string> EquippedBy(PlayerController player)
    {
        var names = new List<string>();
        foreach (ShiftStone? stone in player.PlayerShiftstones.GetEquippedShiftstones())
        {
            if (stone != null) names.Add(stone.gameObject.name.Replace("(Clone)", "", StringComparison.Ordinal).Trim());
        }

        return names;
    }

    private static string Squash(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
