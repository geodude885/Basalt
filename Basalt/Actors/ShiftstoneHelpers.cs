using Il2CppRUMBLE.Combat.ShiftStones;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;

namespace Basalt.Actors;

internal static class ShiftstoneHelpers
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

    // The chart's stones for an actor. Attached as Remote
    public static void AttachTo(Actor actor, PlayerController to)
    {
        var simulatedAs = to.controllerType;
        to.controllerType = ControllerType.Remote;

        try
        {
            var pools = actor.Stones.Select(ToIdOrDefault).ToList();
            foreach ((var pool, var slot) in pools.Select((p, slot) => (p, slot)))
            {
                if (pool == null)
                {
                    Logger.Warning($"[Actor.UnknownStone] {actor}: no shiftstone called \"{actor.Stones[slot]}\"");
                    continue;
                }

                var copy = PoolManager.instance.GetPooledObject(pool)?.GetComponent<ShiftStone>();
                if (copy == null)
                {
                    Logger.Warning($"[Actor.StoneNotPooled] {actor}: {pool} is not in a pool");
                    continue;
                }

                to.PlayerShiftstones.AttachShiftStone(copy, slot, false, false);
            }
        }
        catch (Exception e)
        {
            Logger.Error($"[Actor.StonesFailed] {actor}: {e.Message}");
        }
        finally
        {
            to.controllerType = simulatedAs;
        }
    }

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
