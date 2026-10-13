using System.Numerics;

namespace BossBattles.Services.Charts;

/// <summary>
/// An upright place in the world: a floor <see cref="Position"/> and the compass <see cref="Yaw"/> it faces
/// (degrees, Unity: 0 = +Z, clockwise from above). Local x is to its right, z ahead; Unity is left-handed.
/// </summary>
public readonly record struct Anchor(Vector3 Position, float Yaw)
{
    private float Radians => Yaw * MathF.PI / 180f;

    public Vector3 Forward => new(MathF.Sin(Radians), 0f, MathF.Cos(Radians));

    public Vector3 Right => new(MathF.Cos(Radians), 0f, -MathF.Sin(Radians));

    /// <summary>An anchor facing <paramref name="direction"/>; its tilt is ignored, and no direction faces +Z.</summary>
    public static Anchor Facing(Vector3 position, Vector3 direction) =>
        new(position, (direction.X * direction.X) + (direction.Z * direction.Z) < 1e-6f ? 0f : Normalise(MathF.Atan2(direction.X, direction.Z) * 180f / MathF.PI));

    public Vector3 ToWorld(Vector3 local) => Position + (Right * local.X) + (Vector3.UnitY * local.Y) + (Forward * local.Z);

    public Vector3 ToLocal(Vector3 world)
    {
        Vector3 offset = world - Position;
        return new Vector3(Vector3.Dot(offset, Right), offset.Y, Vector3.Dot(offset, Forward));
    }

    /// <summary>A world yaw as degrees from facing the way the anchor does.</summary>
    public float ToLocalYaw(float worldYaw) => Normalise(worldYaw - Yaw);

    public float ToWorldYaw(float localYaw) => Normalise(localYaw + Yaw);

    private static float Normalise(float degrees)
    {
        degrees %= 360f;
        return degrees < 0f ? degrees + 360f : degrees;
    }
}
