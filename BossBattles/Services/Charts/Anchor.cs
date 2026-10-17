using System.Numerics;

namespace BossBattles.Services.Charts;

/// <summary>
/// An upright place in the world: a floor <see cref="Position"/> and the compass <see cref="Yaw"/> it faces
/// (degrees, Unity: 0 = +Z, clockwise from above). Local x is to its right, z ahead; Unity is left-handed.
/// </summary>
public readonly record struct Anchor(Vector3 Position, float Yaw)
{
    public Vector3 Forward => Flat(Yaw);

    public Vector3 Right => Flat(Yaw + 90f);

    /// <summary>An anchor facing <paramref name="direction"/>; its tilt is ignored, and no direction faces +Z.</summary>
    public static Anchor Facing(Vector3 position, Vector3 direction) =>
        new(position, Degrees.Bearing(direction.X, direction.Z));

    public Vector3 ToWorld(Vector3 local) => Position + (Right * local.X) + (Vector3.UnitY * local.Y) + (Forward * local.Z);

    public Vector3 ToLocal(Vector3 world)
    {
        var offset = world - Position;
        return new Vector3(Vector3.Dot(offset, Right), offset.Y, Vector3.Dot(offset, Forward));
    }

    /// <summary>A world yaw as degrees from facing the way the anchor does.</summary>
    public float ToLocalYaw(float worldYaw) => Degrees.Wrap(worldYaw - Yaw);

    public float ToWorldYaw(float localYaw) => Degrees.Wrap(localYaw + Yaw);

    /// <summary>World yaw for an actor spot's facing, with the anchor at your end of a lane: 0 looks back at you, positive to its right.</summary>
    public float ActorYaw(float facing) => ToWorldYaw(180f + facing);

    private static Vector3 Flat(float bearing)
    {
        (var x, var z) = Degrees.Direction(bearing);
        return new Vector3(x, 0f, z);
    }
}
