using BossBattles.Services.Charts;
using Il2CppPhoton.Pun;
using Il2CppRUMBLE.Players;
using UnityEngine;
using UnityEngine.SceneManagement;
using SceneManager = Il2CppRUMBLE.Managers.SceneManager;

namespace BossBattles.Arenas;

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

    /// <summary>In a park or a match: an online room, where actors (extra Local players) must never be.</summary>
    public static bool Online => PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode;

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

    /// <summary>What a chart's arena spot is measured from: a match map's centre spot, the Gym's origin, or null for "here".</summary>
    public static Vector3? MapOrigin(ArenaKind kind)
    {
        if (kind == ArenaKind.Here) return null;

        GameObject? spots = kind == ArenaKind.Gym ? null : GameObject.Find("ArenaLocation");
        return spots != null && spots.transform.childCount > 0 ? spots.transform.GetChild(0).position : Vector3.zero;
    }

    /// <summary>The lane: from the chart's spot on its map when it has one, else from where you stand, facing the chart's way or yours.</summary>
    public static Anchor? Frame(ArenaKind kind, ArenaSpec spec, Vector3 yourFloor, Vector3 yourFacing)
    {
        Vector3? bearing = spec.Facing is { } degrees ? VectorHelpers.Compass(degrees) : null;
        if (kind == ArenaKind.Here || !spec.HasSpot) return Anchor.Facing(StringHelpers.ToNumerics(yourFloor), StringHelpers.ToNumerics(bearing ?? yourFacing));

        Vector3 origin = MapOrigin(kind) ?? Vector3.zero;
        var spot = new Vector3(origin.x + spec.X!.Value, origin.y, origin.z + spec.Z!.Value);
        if (!TryFloorHeight(spot, out var floorY))
        {
            Logger.Warning($"[Arena.SpotOffFloor] {kind}: nothing to stand on at the chart's spot {StringHelpers.Vector(spot)} - check arena.at");
            return null;
        }

        spot.y = floorY;
        Logger.Debug($"[Arena.Lane] {kind}: chart spot {StringHelpers.Vector(spot)} facing {spec.Facing ?? 0:0}deg");
        return Anchor.Facing(StringHelpers.ToNumerics(spot), StringHelpers.ToNumerics(bearing ?? Vector3.forward));
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
