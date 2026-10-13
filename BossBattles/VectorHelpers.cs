using UnityEngine;

namespace BossBattles;

internal static class VectorHelpers
{
    public static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude < 1e-4f ? Vector3.forward : v.normalized;
    }

    /// <summary>A compass bearing in degrees (0 = +Z, 90 = +X) as a flat vector.</summary>
    public static Vector3 Compass(float degrees) => Quaternion.Euler(0f, degrees, 0f) * Vector3.forward;

    /// <summary>A flat direction's compass bearing, 0 to 360.</summary>
    public static float Bearing(Vector3 direction)
    {
        var degrees = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        return degrees < 0f ? degrees + 360f : degrees;
    }

}
