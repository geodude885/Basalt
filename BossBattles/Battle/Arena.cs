using BossBattles.Services.Charts;
using Il2CppRUMBLE.Players;
using UnityEngine;
using UnityEngine.SceneManagement;
using SceneManager = Il2CppRUMBLE.Managers.SceneManager;

namespace BossBattles.Battle;

/// <summary>
/// Loads the match maps solo, with the game's own <c>SceneManager.LoadSceneAsync</c>, and finds
/// where a lane goes in them. <see cref="Patches.ArenaPatches"/> stops the match handler sending you back.
/// </summary>
internal static class Arena
{
    public const string GymScene = "Gym";
    private const int GymIndex = 1;
    private const int RingIndex = 3;
    private const int PitIndex = 4;
    private const float LoadFadeSeconds = 2f;

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

    public static bool IsMatchMap(ArenaKind kind) => kind is ArenaKind.Ring or ArenaKind.Pit;

    /// <summary>Whether a ray hit is the ground: not a trigger, a player, or one of the mod's own walls.</summary>
    public static bool IsGround(Collider? collider) =>
        collider != null && !collider.isTrigger && !IsOurs(collider) && collider.GetComponentInParent<PlayerController>() == null;

    /// <summary>The mod's own objects are all named BossBattles_.</summary>
    public static bool IsOurs(Collider collider) => collider.gameObject.name.StartsWith("BossBattles_", StringComparison.Ordinal);

    /// <summary>The chart map you are on, or Here for any other scene.</summary>
    public static ArenaKind CurrentMap()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        return new[] { ArenaKind.Gym, ArenaKind.Ring, ArenaKind.Pit }.FirstOrDefault(k => SceneFor(k) == scene);
    }

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
            Logger.Error("[Arena.LoadFailed] no SceneManager to load with");
            return;
        }

        scenes.LoadSceneAsync(index, false, false, LoadFadeSeconds, LoadSceneMode.Single, null);
        Logger.Info($"[Arena.Loading] scene {index}");
    }

    /// <summary>The two match spots of a loaded Ring or Pit, or null when they cannot be found.</summary>
    public static (Transform You, Transform Them)? MatchSpots()
    {
        var locations = GameObject.Find("ArenaLocation");
        return locations == null || locations.transform.childCount < 2
            ? null
            : (locations.transform.GetChild(0), locations.transform.GetChild(1));
    }

    /// <summary>What a chart's arena spot is measured from: a match map's centre, the Gym's origin, or null for "here".</summary>
    public static Vector3? Anchor(ArenaKind kind) => kind == ArenaKind.Here ? null : kind == ArenaKind.Gym ? Vector3.zero : MatchSpots()?.You.position ?? Vector3.zero;

    /// <summary>
    /// The lane: from the chart's spot when it has one; otherwise from your feet here and in the Gym,
    /// and across the floor through the centre in the Ring and Pit.
    /// </summary>
    public static LaneFrame? Frame(ArenaKind kind, ArenaSpec spec, Lane lane, Vector3 yourFloor, Vector3 yourFacing)
    {
        return kind != ArenaKind.Here && spec.HasSpot
            ? SpotFrame(kind, spec, lane)
            : kind is ArenaKind.Here or ArenaKind.Gym
            ? LaneFrame.From(StringHelpers.ToNumerics(yourFloor), StringHelpers.ToNumerics(spec.Facing is { } f ? VectorHelpers.Compass(f) : yourFacing))
            : FittedFrame(kind, spec, lane, yourFloor, yourFacing);
    }

    private static LaneFrame? SpotFrame(ArenaKind kind, ArenaSpec spec, Lane lane)
    {
        Vector3 anchor = Anchor(kind) ?? Vector3.zero;
        var spot = new Vector3(anchor.x + spec.X!.Value, anchor.y, anchor.z + spec.Z!.Value);

        if (!TryFloorHeight(spot, out var floorY))
        {
            Logger.Warning($"[Arena.SpotOffFloor] {kind}: nothing to stand on at the chart's spot {StringHelpers.Vector(spot)} - check arena.at");
            return null;
        }

        spot.y = floorY;
        Vector3 towardCentre = anchor - spot;
        towardCentre.y = 0f;
        Vector3 direction = spec.Facing is { } facing ? VectorHelpers.Compass(facing)
            : towardCentre.sqrMagnitude > 0.25f ? towardCentre.normalized
            : Vector3.forward;

        Vector3 far = spot + (direction * lane.Length);
        if (!TryFloorHeight(far, out _))
            Logger.Debug($"[Arena.LaneOffFloor] {kind}: the lane's far end {StringHelpers.Vector(far)} is off the floor - the actor may fall");

        Logger.Debug($"[Arena.Lane] {kind}: chart spot {StringHelpers.Vector(spot)} facing {VectorHelpers.Bearing(direction):0}deg, {lane.Length:0.0} m");
        return LaneFrame.From(StringHelpers.ToNumerics(spot), StringHelpers.ToNumerics(direction));
    }

    private static LaneFrame? FittedFrame(ArenaKind kind, ArenaSpec spec, Lane lane, Vector3 yourFloor, Vector3 yourFacing)
    {
        (Transform You, Transform Them)? spots = MatchSpots();
        if (spots != null)
        {
            Vector3 you = spots.Value.You.position;
            Vector3 them = spots.Value.Them.position;
            if (Vector2.Distance(new Vector2(you.x, you.z), new Vector2(them.x, them.z)) >= MinimumArenaLane)
                return LaneFrame.Between(StringHelpers.ToNumerics(you), StringHelpers.ToNumerics(them), lane.Length);

            // Solo, both spots sit on the arena's centre.
            Logger.Debug($"[Arena.SpotsTogether] {kind}: the match spots are {StringHelpers.Vector(you)} and {StringHelpers.Vector(them)} - measuring the arena floor instead");
        }
        else
        {
            Logger.Warning($"[Arena.NoMatchSpots] {kind}: no ArenaLocation in {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} - measuring the arena floor instead");
        }

        Vector3 centre = spots?.You.position ?? Vector3.zero;
        Vector3 towardCentre = centre - yourFloor;
        towardCentre.y = 0f;
        Vector3 direction = spec.Facing is { } facing ? VectorHelpers.Compass(facing)
            : towardCentre.sqrMagnitude > 0.25f ? towardCentre.normalized
            : VectorHelpers.Flat(yourFacing);

        // Last resort: from where the map put you, towards the centre, at the chart's own length.
        var fromYou = LaneFrame.From(StringHelpers.ToNumerics(yourFloor), StringHelpers.ToNumerics(direction));
        if (!TryArenaFloor(centre, out Bounds floor)) return fromYou;

        // Across the floor through its centre, kept clear of the edge; the arena may shrink a lane, never stretch it.
        var radius = Mathf.Min(Mathf.Min(floor.extents.x, floor.extents.z) - EdgeMargin, lane.Length / 2f);
        if (radius < MinimumArenaLane / 2f)
        {
            Logger.Info($"[Arena.FloorTooSmall] {kind}: the floor under the centre is only {floor.size.x:0.0} x {floor.size.z:0.0} m");
            return fromYou;
        }

        var floorCentre = new Vector3(centre.x, floor.max.y, centre.z);
        Vector3 near = floorCentre - (direction * radius);
        Vector3 far = floorCentre + (direction * radius);
        Logger.Debug($"[Arena.Lane] {kind}: floor {StringHelpers.Vector(floor.center)} extents {StringHelpers.Vector(floor.extents)}; lane {StringHelpers.Vector(near)} -> {StringHelpers.Vector(far)} ({radius * 2f:0.0} m)");
        return LaneFrame.Between(StringHelpers.ToNumerics(near), StringHelpers.ToNumerics(far), lane.Length);
    }

    /// <summary>The height of the ground under a point, looking down from well above it.</summary>
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

    /// <summary>The bounds of the floor collider under a point: the arena platform.</summary>
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
        Logger.Warning($"[Arena.NoFloor] nothing to stand on under the arena centre {StringHelpers.Vector(centre)}");
        return false;
    }

    /// <summary>The layer of the floor under a point, which players and structures already collide with.</summary>
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

        Logger.Warning($"[Arena.NoFloor] nothing but players under {StringHelpers.Vector(near)} - backstop on the default layer");
        return 0;
    }
}
