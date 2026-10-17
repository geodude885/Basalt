namespace BossBattles.Services;

/// <summary>Compass degrees as Unity has them: 0 = +Z, clockwise from above, kept to 0..360.</summary>
public static class Degrees
{
    public static float Wrap(float degrees)
    {
        degrees %= 360f;
        return degrees < 0f ? degrees + 360f : degrees;
    }

    /// <summary>Between two angles the short way round: 350 to 10 passes through 0, not 180.</summary>
    public static float Lerp(float from, float to, float t) => Wrap(from + ((Wrap(to - from + 180f) - 180f) * t));

    /// <summary>The bearing of a flat direction; no direction at all is 0.</summary>
    public static float Bearing(float x, float z) => (x * x) + (z * z) < 1e-6f ? 0f : Wrap(MathF.Atan2(x, z) * 180f / MathF.PI);

    /// <summary>The flat unit direction a bearing points.</summary>
    public static (float X, float Z) Direction(float bearing)
    {
        var radians = bearing * MathF.PI / 180f;
        return (MathF.Sin(radians), MathF.Cos(radians));
    }
}
