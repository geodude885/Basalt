using Basalt.Services.Charts;
using Il2CppRUMBLE.MoveSystem;
using UnityEngine;

namespace Basalt.Battle;

// Break structures after n seconds, or when behind lane back wall
internal class StructureSweeper
{
    private const float BehindMargin = 0.05f;

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

    public void Update(Rules rules, Anchor? lane)
    {
        var afterSeconds = rules.CleanupSeconds;
        var behind = rules.BreakBehind ? lane : null;
        if ((afterSeconds <= 0 && behind == null) || Time.time < _nextSweep) return;
        _nextSweep = Time.time + 0.25f;

        var combat = CombatManager.instance;
        if (combat == null) return;

        var seen = new HashSet<IntPtr>();
        foreach (var structure in combat.Structures.ToArray())
        {
            if (structure == null || !structure.gameObject.activeInHierarchy) continue;

            if (behind is { } frame && IsBehindLane(structure, frame))
            {
                Break(structure, "behind the lane", sound: false);
                continue;
            }

            if (afterSeconds <= 0) continue;
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

            Break(structure, $"still for {afterSeconds:0.#}s", sound: true);
            _still.Remove(structure.Pointer);
        }

        foreach (var gone in _still.Keys.Where(k => !seen.Contains(k)).ToList()) _still.Remove(gone);
    }

    private static bool IsBehindLane(Structure structure, Anchor lane)
    {
        var name = structure.gameObject.name;
        if (!PlayerStructures.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return false;

        var front = float.NegativeInfinity;
        foreach (var collider in structure.GetComponentsInChildren<Collider>())
        {
            if (collider == null || !collider.enabled || collider.isTrigger) continue;

            var bounds = collider.bounds;
            for (var corner = 0; corner < 8; corner++)
            {
                var point = bounds.center + Vector3.Scale(bounds.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                front = Math.Max(front, lane.ToLocal(point.ToNumerics()).Z);
            }
        }

        return front is > float.NegativeInfinity and < -BehindMargin;
    }

    private static void Break(Structure structure, string why, bool sound)
    {
        try
        {
            structure.Kill(Vector3.zero, sound, true, false);
            Logger.Debug($"[Battle.CleanedUp] {structure.gameObject.name} {why} - broken");
        }
        catch (Exception e)
        {
            Logger.Error($"[Battle.CleanupFailed] {structure.gameObject.name}: {e.Message}");
        }
    }
}
