using Basalt.Services;
using Basalt.Services.Charts;
using UnityEngine;

namespace Basalt;

internal static class UnityVectorExtensions
{
    public static System.Numerics.Vector3 ToNumerics(this Vector3 v) => new(v.x, v.y, v.z);

    public static Vector3 ToUnity(this System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    public static string Describe(this Vector3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";

    public static Vector3 Flat(this Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude < 1e-4f ? Vector3.forward : v.normalized;
    }

    public static float Bearing(this Vector3 direction) => AngleHelpers.Bearing(direction.x, direction.z);

    // Give anchor and a point relative to anchor, returns relative to world
    public static Vector3 ToWorld(this Anchor anchor, float x, float y, float z) => anchor.ToWorld(new System.Numerics.Vector3(x, y, z)).ToUnity();

    // Give anchor and a point relative to world, returns relative to anchor
    public static Vector3 ToLocal(this Anchor anchor, Vector3 world) => anchor.ToLocal(world.ToNumerics()).ToUnity();
}
