using Il2CppRUMBLE.MoveSystem;
using UnityEngine;

namespace Basalt.Arenas;

// Lane collision only
internal class Boundary
{

    public const float Height = 8f;
    public const float Thickness = 2f;

    // Gaps between lane edge and collider
    public const float SideGap = 0.3f;
    public const float EndGap = 0.75f;
    public const float PlayerEndGap = 0.3f;

    private const float ReapplySeconds = 0.1f;
    private const float FullReapplySeconds = 1f;

    private readonly List<Collider> _walls = [];
    private readonly List<Collider> _zoneWalls = [];
    private readonly Dictionary<IntPtr, (bool Active, Collider[] Colliders)> _seen = [];
    private float _nextReapply;
    private float _nextFullReapply;

    // Invisible walls!
    public void Wall(string name, Vector3 centre, Quaternion rotation, Vector3 size, int layer, bool zone = false)
    {
        var wall = GameHelpers.Shape(PrimitiveType.Cube, name, centre, rotation, size);
        wall.layer = layer;
        wall.GetComponent<Renderer>().enabled = false;

        var collider = wall.GetComponent<Collider>();
        _walls.Add(collider);
        if (zone) _zoneWalls.Add(collider);
    }

    public void LetStructuresThrough()
    {
        if (_walls.Count == 0 || Time.time < _nextReapply) return;
        _nextReapply = Time.time + ReapplySeconds;
        var everything = Time.time >= _nextFullReapply;
        if (everything) _nextFullReapply = Time.time + FullReapplySeconds;

        var combat = CombatManager.instance;
        if (combat == null) return;

        foreach (var structure in combat.Structures)
        {
            if (structure == null) continue;

            var active = structure.gameObject.activeInHierarchy;
            if (!_seen.TryGetValue(structure.Pointer, out var known) || known.Colliders.Length == 0)
                known = (false, structure.GetComponentsInChildren<Collider>(true).ToArray());

            if (active && (everything || !known.Active)) Ignore(known.Colliders, _walls);
            _seen[structure.Pointer] = (active, known.Colliders);
        }
    }

    public void LetActorsThrough(IEnumerable<Collider> actorColliders) => Ignore(actorColliders, _zoneWalls);

    private static void Ignore(IEnumerable<Collider> colliders, List<Collider> walls)
    {
        foreach (var collider in colliders.Where(c => c != null))
        {
            foreach (var wall in walls.Where(w => w != null)) Physics.IgnoreCollision(collider, wall, true);
        }
    }
    public void Destroy()
    {
        foreach (var wall in _walls.Where(w => w != null)) UnityEngine.Object.Destroy(wall.gameObject);
        Forget();
    }

    public void Forget()
    {
        _walls.Clear();
        _zoneWalls.Clear();
        _seen.Clear();
    }
}
