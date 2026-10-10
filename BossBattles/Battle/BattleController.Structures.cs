using BossBattles.Core;
using Il2CppRUMBLE.MoveSystem;
using UnityEngine;

namespace BossBattles.Battle;

/// <summary>Structures on the map: cleared before a battle, and broken when left standing still.</summary>
internal sealed partial class BattleController
{
    private static readonly string[] PlayerStructures = { "Disc", "Ball", "RockCube", "Pillar", "Wall" };

    private readonly Dictionary<IntPtr, (Vector3 At, float Since)> _still = [];
    private float _nextCleanup;

    public static int LastCleared { get; private set; }

    public int CleanedUp { get; private set; }

    /// <summary>Breaks every structure a player spawned (scenery stays) with the game's own <c>Structure.Kill</c>.</summary>
    private static void ClearStructures()
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
                Mod.Note("Battle.ClearFailed", $"{name}: {e.Message}");
            }
        }

        LastCleared = cleared;
        Mod.Note("Battle.Cleared", $"{cleared} structure(s) cleared off the map", echo: false);
    }

    /// <summary>
    /// The chart's cleanup rule: a structure still for <see cref="Services.Charts.Rules.CleanupSeconds"/> breaks, so a
    /// wall grounded in front of you cannot soak up a whole section. Held (kinematic) projectiles are left.
    /// </summary>
    private void CleanUpStillStructures()
    {
        var after = Chart?.Rules.CleanupSeconds ?? 0;
        if (after <= 0 || Time.time < _nextCleanup) return;
        _nextCleanup = Time.time + 0.25f;

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

            if (Time.time - still.Since < after) continue;

            try
            {
                structure.Kill(at, true, true, false);
                CleanedUp++;
                Mod.Note("Battle.CleanedUp", $"{structure.gameObject.name} still for {after:0.#}s - broken", echo: false);
            }
            catch (Exception e)
            {
                Mod.Note("Battle.CleanupFailed", $"{structure.gameObject.name}: {e.Message}");
            }

            _still.Remove(structure.Pointer);
        }

        foreach (IntPtr gone in _still.Keys.Where(k => !seen.Contains(k)).ToList()) _still.Remove(gone);
    }
}
