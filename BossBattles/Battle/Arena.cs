using BossBattles.Actors;
using BossBattles.Core;
using BossBattles.Services.Charts;
using Il2CppRUMBLE.Players;
using UnityEngine;
using UnityEngine.SceneManagement;
using SceneManager = Il2CppRUMBLE.Managers.SceneManager;

namespace BossBattles.Battle;

/// <summary>
/// Loading the match maps solo and finding where a lane goes in them. Both maps have
/// <c>Logic/ArenaLocation/PlayerOneLocation</c> and <c>PlayerTwoLocation</c> - the two match spots -
/// which make the lane's ends. Loading is the game's own <c>SceneManager.LoadSceneAsync</c>, as
/// MultiplayerMapsInSinglePlayer does; <see cref="Patches.ArenaPatches"/> stops the match handler
/// sending you straight back.
/// </summary>
internal static class Arena
{
    public const string GymScene = "Gym";
    private const int GymIndex = 1;
    private const int RingIndex = 3;
    private const int PitIndex = 4;
    private const float LoadFadeSeconds = 2f;

    /// <summary>Match spots closer than this are not a lane.</summary>
    private const float MinimumArenaLane = 2f;

    /// <summary>How far the lane ends stay inside the arena floor's edge.</summary>
    private const float EdgeMargin = 0.8f;

    public static string? SceneFor(ArenaKind kind) => kind switch
    {
        ArenaKind.Gym => GymScene,
        ArenaKind.Ring => "Map0",
        ArenaKind.Pit => "Map1",
        _ => null,
    };

    /// <summary>The Ring or Pit - a match map, which the match handler would otherwise send you back from.</summary>
    public static bool IsMatchMap(ArenaKind kind) => kind is ArenaKind.Ring or ArenaKind.Pit;

    /// <summary>
    /// Whether a ray hit is the ground: not a trigger, not a player, and not one of the mod's own
    /// invisible walls - the zone's ceiling once stood in for the floor and spawned walls 3 m up.
    /// </summary>
    public static bool IsGround(Collider? collider) =>
        collider != null && !collider.isTrigger && !IsOurs(collider) && collider.GetComponentInParent<PlayerController>() == null;

    /// <summary>The mod's own parts - lane lines, walls, ceilings, markers - all named BossBattles_.</summary>
    public static bool IsOurs(Collider collider) => collider.gameObject.name.StartsWith("BossBattles_", StringComparison.Ordinal);

    public static bool InScene(ArenaKind kind) =>
        kind == ArenaKind.Here || string.Equals(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, SceneFor(kind), StringComparison.Ordinal);

    public static void Load(ArenaKind kind) => LoadScene(kind switch
    {
        ArenaKind.Ring => RingIndex,
        ArenaKind.Pit => PitIndex,
        _ => GymIndex,
    });

    public static void LoadGym() => LoadScene(GymIndex);

    private static void LoadScene(int index)
    {
        SceneManager? scenes = SceneManager.instance ?? UnityEngine.Object.FindObjectOfType<SceneManager>();
        if (scenes == null)
        {
            Mod.Note("Arena.LoadFailed", "no SceneManager to load with");
            return;
        }

        scenes.LoadSceneAsync(index, false, false, LoadFadeSeconds, LoadSceneMode.Single, null);
        Mod.Note("Arena.Loading", $"scene {index}");
    }

    /// <summary>The two match spots of a loaded Ring or Pit, or null when they cannot be found.</summary>
    public static (Transform You, Transform Them)? MatchSpots()
    {
        var locations = GameObject.Find("ArenaLocation");
        return locations == null || locations.transform.childCount < 2
            ? null
            : (locations.transform.GetChild(0), locations.transform.GetChild(1));
    }

    /// <summary>
    /// Logs every transform in the match map that could mark the two players' ends - the first
    /// Ring run found both ArenaLocation children at one point - so the right ones can be chosen
    /// from data. Includes inactive objects (MultiplayerMapsInSinglePlayer hides the pedestals).
    /// </summary>
    public static void DumpSpotCandidates()
    {
        var logic = GameObject.Find("Logic");
        if (logic == null)
        {
            Mod.Note("Arena.Spots", "no Logic root in this scene");
            return;
        }

        foreach (var path in new[] { "ArenaLocation", "Pedestals", "SpawnPointHandler", "SpawntransformSlabOne", "SpawntransformSlabTwo" })
        {
            Transform? root = logic.transform.Find(path);
            if (root == null)
            {
                Mod.Note("Arena.Spots", $"{path}: missing", echo: false);
                continue;
            }

            var lines = new List<string> { $"{path} {Describe.Vector(root.position)}{(root.gameObject.activeInHierarchy ? "" : " (inactive)")}" };
            for (var i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                lines.Add($"{child.name} {Describe.Vector(child.position)}{(child.gameObject.activeSelf ? "" : " (inactive)")}");
            }

            Mod.Note("Arena.Spots", string.Join(" | ", lines), echo: false);
        }
    }

    /// <summary>
    /// What a chart's arena spot is measured from: the Ring's or Pit's centre (where both match
    /// spots sit when solo), the Gym's world origin, or null for "here".
    /// </summary>
    public static Vector3? Anchor(ArenaKind kind) => kind == ArenaKind.Here ? null : kind == ArenaKind.Gym ? Vector3.zero : MatchSpots()?.You.position ?? Vector3.zero;

    /// <summary>A compass direction in degrees (0 = world +Z, 90 = +X) as a flat vector.</summary>
    public static Vector3 Compass(float degrees) => Quaternion.Euler(0f, degrees, 0f) * Vector3.forward;

    /// <summary>A flat direction's compass bearing, 0 to 360.</summary>
    public static float Bearing(Vector3 direction)
    {
        var degrees = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        return degrees < 0f ? degrees + 360f : degrees;
    }

    /// <summary>
    /// The lane for a battle in <paramref name="kind"/>, with the chart's own spot when it has one:
    /// from there, along its facing (or towards the centre), at the chart's own length. Without a
    /// spot: here and the Gym from your feet; the Ring and Pit across the floor through the centre.
    /// </summary>
    public static LaneFrame? Frame(ArenaKind kind, ArenaSpec spec, Lane lane, Vector3 yourFloor, Vector3 yourFacing)
    {
        return kind != ArenaKind.Here && spec.HasSpot
            ? SpotFrame(kind, spec, lane)
            : kind is ArenaKind.Here or ArenaKind.Gym
            ? LaneFrame.From(Describe.ToNumerics(yourFloor), Describe.ToNumerics(spec.Facing is { } f ? Compass(f) : yourFacing))
            : FittedFrame(kind, spec, lane, yourFloor, yourFacing);
    }

    private static LaneFrame? SpotFrame(ArenaKind kind, ArenaSpec spec, Lane lane)
    {
        Vector3 anchor = Anchor(kind) ?? Vector3.zero;
        var spot = new Vector3(anchor.x + spec.X!.Value, anchor.y, anchor.z + spec.Z!.Value);

        if (!TryFloorHeight(spot, out var floorY))
        {
            Mod.Note("Arena.SpotOffFloor", $"{kind}: nothing to stand on at the chart's spot {Describe.Vector(spot)} - check arena.at");
            return null;
        }

        spot.y = floorY;
        Vector3 towardCentre = anchor - spot;
        towardCentre.y = 0f;
        Vector3 direction = spec.Facing is { } facing ? Compass(facing)
            : towardCentre.sqrMagnitude > 0.25f ? towardCentre.normalized
            : Vector3.forward;

        Vector3 far = spot + (direction * lane.Length);
        if (!TryFloorHeight(far, out _))
            Mod.Note("Arena.LaneOffFloor", $"{kind}: the lane's far end {Describe.Vector(far)} is off the floor - the actor may fall", echo: false);

        Mod.Note("Arena.Lane", $"{kind}: chart spot {Describe.Vector(spot)} facing {Bearing(direction):0}deg, {lane.Length:0.0} m", echo: false);
        return LaneFrame.From(Describe.ToNumerics(spot), Describe.ToNumerics(direction));
    }

    private static LaneFrame? FittedFrame(ArenaKind kind, ArenaSpec spec, Lane lane, Vector3 yourFloor, Vector3 yourFacing)
    {
        DumpSpotCandidates();

        (Transform You, Transform Them)? spots = MatchSpots();
        if (spots != null)
        {
            Vector3 you = spots.Value.You.position;
            Vector3 them = spots.Value.Them.position;
            if (Vector2.Distance(new Vector2(you.x, you.z), new Vector2(them.x, them.z)) >= MinimumArenaLane)
                return LaneFrame.Between(Describe.ToNumerics(you), Describe.ToNumerics(them), lane.Length);

            // Solo, both spots sit on the arena's centre (measured in the Ring): no lane between them.
            Mod.Note("Arena.SpotsTogether", $"{kind}: the match spots are {Describe.Vector(you)} and {Describe.Vector(them)} - measuring the arena floor instead", echo: false);
        }
        else
        {
            Mod.Note("Arena.NoMatchSpots", $"{kind}: no ArenaLocation in {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} - measuring the arena floor instead");
        }

        Vector3 centre = spots?.You.position ?? Vector3.zero;
        Vector3 towardCentre = centre - yourFloor;
        towardCentre.y = 0f;
        Vector3 direction = spec.Facing is { } facing ? Compass(facing)
            : towardCentre.sqrMagnitude > 0.25f ? towardCentre.normalized
            : ActorRig.Flat(yourFacing);

        // Across the arena floor through its centre, the way you came in, kept clear of the edge:
        // the chart's lane is fitted to that diameter.
        if (TryArenaFloor(centre, out Bounds floor))
        {
            // The arena may only shrink a lane, never stretch it: the Ring's floor collider measured
            // 22.7 m square, and doubling a chart's distances is not what its author wrote.
            var radius = Mathf.Min(Mathf.Min(floor.extents.x, floor.extents.z) - EdgeMargin, lane.Length / 2f);
            if (radius >= MinimumArenaLane / 2f)
            {
                var floorCentre = new Vector3(centre.x, floor.max.y, centre.z);
                Vector3 near = floorCentre - (direction * radius);
                Vector3 far = floorCentre + (direction * radius);
                Mod.Note("Arena.Lane", $"{kind}: floor {Describe.Vector(floor.center)} extents {Describe.Vector(floor.extents)}; lane {Describe.Vector(near)} -> {Describe.Vector(far)} ({radius * 2f:0.0} m)", echo: false);
                return LaneFrame.Between(Describe.ToNumerics(near), Describe.ToNumerics(far), lane.Length);
            }

            Mod.Note("Arena.FloorTooSmall", $"{kind}: the floor under the centre is only {floor.size.x:0.0} x {floor.size.z:0.0} m");
        }

        // Last resort: from where the map put you, towards the centre, at the chart's own length.
        return LaneFrame.From(Describe.ToNumerics(yourFloor), Describe.ToNumerics(direction));
    }

    /// <summary>
    /// The way out from a point with the most clear space at chest height, of
    /// <paramref name="directions"/> evenly spread - so a lane laid that way is not run into a
    /// wall. Player colliders and triggers do not count as blocking.
    /// </summary>
    public static (Vector3 Direction, float Clear) OpenestDirection(Vector3 floor, int directions = 16, float reach = 30f)
    {
        Vector3 from = floor + (Vector3.up * 1.2f);
        (Vector3 Direction, float Clear) best = (Vector3.forward, -1f);

        for (var i = 0; i < directions; i++)
        {
            Vector3 direction = Quaternion.Euler(0f, 360f * i / directions, 0f) * Vector3.forward;
            var clear = reach;

            foreach (RaycastHit hit in Physics.RaycastAll(from, direction, reach))
            {
                Collider collider = hit.collider;
                if (!IsGround(collider)) continue;
                clear = Mathf.Min(clear, hit.distance);
            }

            if (clear > best.Clear) best = (direction, clear);
        }

        return best;
    }

    /// <summary>
    /// The first ground within <paramref name="reach"/> below <paramref name="from"/> - a short look
    /// down, for whether something stands on the floor; skipping players and triggers.
    /// </summary>
    public static bool TryGroundBelow(Vector3 from, float reach, out float height)
    {
        foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, reach).OrderBy(h => h.distance))
        {
            Collider collider = hit.collider;
            if (!IsGround(collider)) continue;

            height = hit.point.y;
            return true;
        }

        height = from.y;
        return false;
    }

    /// <summary>The height of the ground under a point (from well above it), skipping players and triggers.</summary>
    public static bool TryFloorHeight(Vector3 point, out float height)
    {
        foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(point.x, point.y + 20f, point.z), Vector3.down, 60f).OrderBy(h => h.distance))
        {
            Collider collider = hit.collider;
            if (!IsGround(collider)) continue;

            height = hit.point.y;
            return true;
        }

        height = point.y;
        return false;
    }

    /// <summary>The bounds of the floor collider under a point - the arena platform, skipping players and triggers.</summary>
    private static bool TryArenaFloor(Vector3 centre, out Bounds floor)
    {
        foreach (RaycastHit hit in Physics.RaycastAll(centre + (Vector3.up * 3f), Vector3.down, 10f).OrderBy(h => h.distance))
        {
            Collider collider = hit.collider;
            if (!IsGround(collider)) continue;

            floor = collider.bounds;
            return true;
        }

        floor = default;
        Mod.Note("Arena.NoFloor", $"nothing to stand on under the arena centre {Describe.Vector(centre)}");
        return false;
    }

    /// <summary>
    /// The layer the floor under a point is on - one players and structures already collide with.
    /// Your own body is in the way of the ray (the first self-test put the backstop on
    /// PlayerHitbox), so player colliders and triggers are skipped.
    /// </summary>
    public static int FloorLayer(Vector3 near)
    {
        RaycastHit[] hits = Physics.RaycastAll(near + (Vector3.up * 1.5f), Vector3.down, 6f);

        foreach (RaycastHit hit in hits.OrderBy(h => h.distance))
        {
            Collider collider = hit.collider;
            if (collider == null || collider.isTrigger || IsOurs(collider)) continue;
            if (collider.GetComponentInParent<PlayerController>() != null) continue;

            return collider.gameObject.layer;
        }

        Mod.Note("Arena.NoFloor", $"nothing but players under {Describe.Vector(near)} - backstop on the default layer");
        return 0;
    }
}
