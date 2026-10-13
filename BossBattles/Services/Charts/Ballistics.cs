using System.Numerics;

namespace BossBattles.Services.Charts;

/// <summary>
/// Launch velocities under gravity that arrive at the target in distance / speed seconds, so a
/// chart's speed still sets how long you have to react.
/// </summary>
public static class Ballistics
{
    /// <param name="from">Launch point.</param>
    /// <param name="to">Target point.</param>
    /// <param name="speed">m/s along the straight line; must be over 0.</param>
    /// <param name="gravity">Downward acceleration, positive (Unity's is 9.81).</param>
    public static Vector3 LaunchVelocity(Vector3 from, Vector3 to, float speed, float gravity)
    {
        if (speed <= 0f) throw new ArgumentOutOfRangeException(nameof(speed), speed, "speed must be over 0");

        Vector3 offset = to - from;
        var distance = offset.Length();
        if (distance < 1e-4f) return Vector3.Zero;

        var time = distance / speed;

        // offset = v t - (g t^2 / 2) up   =>   v = offset / t + (g t / 2) up
        return (offset / time) + (Vector3.UnitY * (gravity * time * 0.5f));
    }
}
