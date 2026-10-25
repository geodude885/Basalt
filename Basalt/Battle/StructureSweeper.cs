using Il2CppRUMBLE.MoveSystem;
using UnityEngine;

namespace Basalt.Battle;

internal class StructureSweeper
{
    private static readonly string[] PlayerStructures = { "Disc", "Ball", "RockCube", "Pillar", "Wall" };

    private readonly Dictionary<IntPtr, (Vector3 At, float Since)> _still = [];
    private float _nextSweep;

    public static void ClearAll()
    {
        var combat = CombatManager.instance;
        if (combat == null) return;

        var cleared = 0;
        foreach (var structure in combat.Structures.ToArray())
        {
            if (structure == null || !structure.gameObject.activeInHierarchy) continue;

            var name = structure.gameObject.name;
            if (!PlayerStructures.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;

            try
            {
                structure.Kill(Vector3.zero, false, true, false);
                cleared++;
            }
            catch (Exception e)
            {
                Logger.Error($"[Battle.ClearFailed] {name}: {e.Message}");
            }
        }

        Logger.Debug($"[Battle.Cleared] {cleared} structure(s) cleared off the map");
    }

    public void Reset() => _still.Clear();

    // Breaks any structure still for <afterSeconds>s
    public void Update(double afterSeconds)
    {
        if (afterSeconds <= 0 || Time.time < _nextSweep) return;
        _nextSweep = Time.time + 0.25f;

        var combat = CombatManager.instance;
        if (combat == null) return;

        var seen = new HashSet<IntPtr>();
        foreach (var structure in combat.Structures.ToArray())
        {
            if (structure == null || !structure.gameObject.activeInHierarchy) continue;
            var body = structure.GetComponent<Rigidbody>();
            if (body == null || body.isKinematic) continue;

            seen.Add(structure.Pointer);
            var at = structure.transform.position;
            if (!_still.TryGetValue(structure.Pointer, out var still) || (at - still.At).sqrMagnitude > 0.05f * 0.05f)
            {
                _still[structure.Pointer] = (at, Time.time);
                continue;
            }

            if (Time.time - still.Since < afterSeconds) continue;

            try
            {
                structure.Kill(at, true, true, false);
                Logger.Debug($"[Battle.CleanedUp] {structure.gameObject.name} still for {afterSeconds:0.#}s - broken");
            }
            catch (Exception e)
            {
                Logger.Error($"[Battle.CleanupFailed] {structure.gameObject.name}: {e.Message}");
            }

            _still.Remove(structure.Pointer);
        }

        foreach (var gone in _still.Keys.Where(k => !seen.Contains(k)).ToList()) _still.Remove(gone);
    }
}
