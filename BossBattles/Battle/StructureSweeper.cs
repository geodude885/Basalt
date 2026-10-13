using Il2CppRUMBLE.MoveSystem;
using UnityEngine;

namespace BossBattles.Battle;

/// <summary>Structures on the map: cleared before a battle, and broken when left standing still.</summary>
internal sealed class StructureSweeper
{
    private static readonly string[] PlayerStructures = { "Disc", "Ball", "RockCube", "Pillar", "Wall" };

    private readonly Dictionary<IntPtr, (Vector3 At, float Since)> _still = [];
    private float _nextSweep;

    /// <summary>Breaks every structure a player spawned (scenery stays) with the game's own <c>Structure.Kill</c>.</summary>
    public static void ClearAll()
    {
        CombatManager? combat = CombatManager.instance;
        if (combat == null) return;

        var cleared = 0;
        foreach (Structure structure in combat.Structures.ToArray())
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

    /// <summary>
    /// The chart's cleanup rule: a structure still for <paramref name="afterSeconds"/> breaks, so a wall grounded in
    /// front of you cannot soak up a whole section. Held (kinematic) projectiles are left.
    /// </summary>
    public void Update(double afterSeconds)
    {
        if (afterSeconds <= 0 || Time.time < _nextSweep) return;
        _nextSweep = Time.time + 0.25f;

        CombatManager? combat = CombatManager.instance;
        if (combat == null) return;

        var seen = new HashSet<IntPtr>();
        foreach (Structure structure in combat.Structures.ToArray())
        {
            if (structure == null || !structure.gameObject.activeInHierarchy) continue;
            Rigidbody body = structure.GetComponent<Rigidbody>();
            if (body == null || body.isKinematic) continue;

            seen.Add(structure.Pointer);
            Vector3 at = structure.transform.position;
            if (!_still.TryGetValue(structure.Pointer, out (Vector3 At, float Since) still) || (at - still.At).sqrMagnitude > 0.05f * 0.05f)
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

        foreach (IntPtr gone in _still.Keys.Where(k => !seen.Contains(k)).ToList()) _still.Remove(gone);
    }
}
