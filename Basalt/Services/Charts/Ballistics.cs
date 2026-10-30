using System.Numerics;

namespace Basalt.Services.Charts;

public static class Ballistics
{
    // Unity gravity is 9.81
    public static Vector3 GetLaunchVelocity(Vector3 from, Vector3 to, float speed, float gravity)
    {
        if (speed <= 0f) throw new ArgumentOutOfRangeException(nameof(speed), speed, "speed must be over 0");

        var offset = to - from;
        var distance = offset.Length();
        if (distance < 1e-4f) return Vector3.Zero;

        var time = distance / speed;

        // offset = v t - (g t^2 / 2) up   =>   v = offset / t + (g t / 2) up
        return (offset / time) + (Vector3.UnitY * (gravity * time * 0.5f));
    }
}
