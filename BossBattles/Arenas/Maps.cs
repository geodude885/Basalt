using BossBattles.Services.Charts;
using Il2CppPhoton.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;
using SceneManager = Il2CppRUMBLE.Managers.SceneManager;

namespace BossBattles.Arenas;

internal static class Maps
{
    public const string GymScene = "Gym";
    private const int GymIndex = 1;
    private const int RingIndex = 3;
    private const int PitIndex = 4;
    private const float LoadFadeSeconds = 2f;

    public static ArenaKind Current => new[] { ArenaKind.Gym, ArenaKind.Ring, ArenaKind.Pit }.FirstOrDefault(k => k.IsLoaded());

    // Use to ensure room is offline
    public static bool Online => PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode;

    public static string? Scene(this ArenaKind kind) => kind switch
    {
        ArenaKind.Gym => GymScene,
        ArenaKind.Ring => "Map0",
        ArenaKind.Pit => "Map1",
        _ => null,
    };

    public static bool IsMatchMap(this ArenaKind kind) => kind is ArenaKind.Ring or ArenaKind.Pit;

    public static bool IsLoaded(this ArenaKind kind) =>
        kind == ArenaKind.Here || string.Equals(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, kind.Scene(), StringComparison.Ordinal);

    public static void Load(this ArenaKind kind) => LoadScene(kind switch
    {
        ArenaKind.Ring => RingIndex,
        ArenaKind.Pit => PitIndex,
        _ => GymIndex,
    });

    public static void LoadGym() => LoadScene(GymIndex);

    public static Vector3? GetArenaOrigin(this ArenaKind kind)
    {
        if (kind == ArenaKind.Here) return null;

        var spots = kind == ArenaKind.Gym ? null : GameObject.Find("ArenaLocation");
        return spots != null && spots.transform.childCount > 0 ? spots.transform.GetChild(0).position : Vector3.zero;
    }

    public static Anchor? CreateLaneAnchor(this ArenaSpec spec, ArenaKind kind, Vector3 yourFeet, Vector3 yourFacing)
    {
        if (kind == ArenaKind.Here || !spec.HasSpot) return new Anchor(yourFeet.ToNumerics(), Degrees.Wrap(spec.Facing ?? yourFacing.Bearing()));

        var origin = kind.GetArenaOrigin() ?? Vector3.zero;
        var spot = new Vector3(origin.x + spec.X!.Value, origin.y, origin.z + spec.Z!.Value);
        if (GameHelpers.HeightUnder(spot) is not { } floor)
        {
            Logger.Warning($"[Arena.SpotOffFloor] {kind}: nothing to stand on at the chart's spot {spot.Describe()} - check arena.at");
            return null;
        }

        spot.y = floor;
        Logger.Debug($"[Arena.Lane] {kind}: chart spot {spot.Describe()} facing {spec.Facing ?? 0:0}deg");
        return new Anchor(spot.ToNumerics(), Degrees.Wrap(spec.Facing ?? 0f));
    }

    private static void LoadScene(int index)
    {
        var scenes = SceneManager.instance ?? UnityEngine.Object.FindObjectOfType<SceneManager>();
        if (scenes == null)
        {
            Logger.Error("[Arena.LoadFailed] no SceneManager to load with");
            return;
        }

        scenes.LoadSceneAsync(index, false, false, LoadFadeSeconds, LoadSceneMode.Single, null);
        Logger.Debug($"[Arena.Loading] scene {index}");
    }
}
