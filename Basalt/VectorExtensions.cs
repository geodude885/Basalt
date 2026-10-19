using UnityEngine;

namespace Basalt;

internal static class VectorExtensions
{
    public static System.Numerics.Vector3 ToNumerics(this Vector3 v) => new(v.x, v.y, v.z);

    public static Vector3 ToUnity(this System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    public static string Describe(this Vector3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";

    /// <summary>Flattened onto the floor and normalised; straight up or down is forward.</summary>
    public static Vector3 Flat(this Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude < 1e-4f ? Vector3.forward : v.normalized;
    }

    public static float Bearing(this Vector3 direction) => Degrees.Bearing(direction.x, direction.z);
}
