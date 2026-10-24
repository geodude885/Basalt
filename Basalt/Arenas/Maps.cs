using Basalt.Services.Charts;
using Il2CppPhoton.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;
using SceneManager = Il2CppRUMBLE.Managers.SceneManager;

namespace Basalt.Arenas;

internal static class Maps
{
    public const string GymScene = "Gym";
    private const int GymIndex = 1;
    private const int RingIndex = 3;
    private const int PitIndex = 4;
    private const float LoadFadeSeconds = 2f;

    public static ArenaKind? Current => Enum.GetValues<ArenaKind>().Select(k => (ArenaKind?)k).FirstOrDefault(k => k!.Value.IsLoaded());
    public static bool IsCurrentlyOnline => PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode;

    public static string Scene(this ArenaKind kind) => kind switch
    {
        ArenaKind.Ring => "Map0",
        ArenaKind.Pit => "Map1",
        _ => GymScene,
    };

    public static bool IsMatchMap(this ArenaKind kind) => kind is ArenaKind.Ring or ArenaKind.Pit;

    public static bool IsLoaded(this ArenaKind kind) =>
        string.Equals(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, kind.Scene(), StringComparison.Ordinal);

    public static void Load(this ArenaKind kind) => LoadScene(kind switch
    {
        ArenaKind.Ring => RingIndex,
        ArenaKind.Pit => PitIndex,
        _ => GymIndex,
    });

    public static void LoadGym() => LoadScene(GymIndex);

    public static Vector3 GetArenaOrigin(this ArenaKind kind)
    {
        var spots = kind == ArenaKind.Gym ? null : GameObject.Find("ArenaLocation");
        return spots != null && spots.transform.childCount > 0 ? spots.transform.GetChild(0).position : Vector3.zero;
    }

    public static Anchor? CreateLaneAnchor(this ArenaSpec spec)
    {
        var origin = spec.Map.GetArenaOrigin();
        var spot = new Vector3(origin.x + spec.X, origin.y, origin.z + spec.Z);
        if (GameHelpers.HeightUnder(spot) is not { } floor)
        {
            Logger.Warning($"[Arena.SpotOffFloor] {spec.Map}: nothing to stand on at the chart's spot {spot.Describe()} - check arena.at");
            return null;
        }

        spot.y = floor;
        Logger.Debug($"[Arena.Lane] {spec.Map}: chart spot {spot.Describe()} facing {spec.Facing:0}deg");
        return new Anchor(spot.ToNumerics(), Degrees.Wrap(spec.Facing));
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
