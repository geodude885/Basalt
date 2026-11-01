using System.Numerics;

namespace Basalt.Services;

public static class AngleHelpers
{
    public static float Wrap(float degrees)
    {
        degrees %= 360f;
        return degrees < 0f ? degrees + 360f : degrees;
    }

    public static float Lerp(float from, float to, float t)
        => Wrap(from + ((Wrap(to - from + 180f) - 180f) * t));

    // 0 degrees is unity's positive Z direction
    public static float Bearing(float x, float z) => (x * x) + (z * z) < 1e-6f ? 0f : Wrap(MathF.Atan2(x, z) * 180f / MathF.PI);

    public static Vector3 VectorFromBearing(float bearing)
    {
        var radians = bearing * MathF.PI / 180f;
        return new Vector3(MathF.Sin(radians), 0f, MathF.Cos(radians));
    }
}
