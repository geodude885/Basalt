using BossBattles.Core;
using Il2Cpp;
using Il2CppRUMBLE.Combat.ShiftStones;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;

namespace BossBattles.Actors;

/// <summary>
/// The game's shiftstones by name: read from the pool manager's resources at runtime (each pooled
/// prefab with a <see cref="ShiftStone"/> on it), so a chart can say "Vigor", "VigorStone" or the
/// stone's own display name and get the pool to fetch it from - no list to keep up to date.
/// </summary>
internal static class Shiftstones
{
    private static List<(string Pool, string Display)>? _catalog;

    /// <summary>Every shiftstone pool, with its display name.</summary>
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

    /// <summary>The pool for a chart's stone name, or null when there is none like it.</summary>
    public static string? Pool(string name)
    {
        var wanted = Squash(name);
        foreach ((var pool, var display) in Catalog())
        {
            if (Squash(pool) == wanted || Squash(pool) == wanted + "stone" || Squash(display) == wanted || Squash(display) == wanted + "stone") return pool;
        }

        return null;
    }

    /// <summary>The stones a player has equipped, by pool name - what a chart made in game stores.</summary>
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
