using Il2CppRUMBLE.Players;
using UnityEngine;

namespace BossBattles;

internal static class GameHelpers
{
    // Positions and colliders

    public static bool ProbablyGround(this Collider? collider) =>
        collider != null && !collider.isTrigger && !collider.IsOurs() && collider.GetComponentInParent<PlayerController>() == null;

    public static bool IsOurs(this Collider collider) => collider.gameObject.name.StartsWith("BossBattles_", StringComparison.Ordinal);

    public static float? HeightUnder(Vector3 point) => FirstGround(new Vector3(point.x, point.y + 20f, point.z), 60f)?.point.y;

    public static int LayerUnder(Vector3 near)
    {
        if (FirstGround(near + (Vector3.up * 1.5f), 6f) is { } hit) return hit.collider.gameObject.layer;

        Logger.Warning($"[Arena.NoFloor] nothing but players under {near.Describe()} - backstop on the default layer");
        return 0;
    }

    private static RaycastHit? FirstGround(Vector3 from, float distance) =>
        Physics.RaycastAll(from, Vector3.down, distance)
            .OrderBy(h => h.distance)
            .Where(h => h.collider.ProbablyGround())
            .Select(h => (RaycastHit?)h)
            .FirstOrDefault();


    // String bits and bobs

    public static string Squash(this string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    public static string Describe(this Player? player) =>
       player == null ? "null-player" : $"{player.Data?.GeneralData?.PublicUsername ?? "?"}(hp={player.Data?.HealthPoints})";


    // Game and transforms

    /// <summary>A plain cube, sized and placed: the lane's walls and markings are built from these.</summary>
    public static GameObject Cube(string name, Vector3 centre, Quaternion rotation, Vector3 size)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetPositionAndRotation(centre, rotation);
        cube.transform.localScale = size;
        return cube;
    }

    public static void DisableAll<T>(this GameObject root)
        where T : Behaviour
    {
        foreach (T behaviour in root.GetComponentsInChildren<T>(true)) behaviour.enabled = false;
    }


}
