using System.Numerics;

namespace BossBattles.Services.Charts;

/// <summary>
/// Launch velocities for projectiles under gravity. A chart gives a projectile a speed and a
/// target; launched straight at the target, gravity would drop a slow one well short. Instead the
/// flight time is the straight-line distance over the speed, and the launch velocity is the one
/// that arrives at the target in that time - so "speed" still sets how long you have to react.
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

    /// <summary>Where a body launched at <paramref name="velocity"/> is after <paramref name="time"/> seconds.</summary>
    public static Vector3 PositionAt(Vector3 from, Vector3 velocity, float gravity, float time) =>
        from + (velocity * time) - (Vector3.UnitY * (0.5f * gravity * time * time));
}
