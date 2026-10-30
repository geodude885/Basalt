using System.Numerics;

namespace Basalt.Services.Charts;

// A relative position and direction
public readonly record struct Anchor(Vector3 Position, float Yaw)
{
    public Vector3 Forward => AngleHelpers.Direction(Yaw);
    public Vector3 Right => AngleHelpers.Direction(Yaw + 90f);

    /// <summary>An anchor facing <paramref name="direction"/>; its tilt is ignored, and no direction faces +Z.</summary>
    public static Anchor Facing(Vector3 position, Vector3 direction) =>
        new(position, AngleHelpers.Bearing(direction.X, direction.Z));

    public Vector3 ToWorld(Vector3 local) => Position + (Right * local.X) + (Vector3.UnitY * local.Y) + (Forward * local.Z);

    public Vector3 ToLocal(Vector3 world)
    {
        var offset = world - Position;
        return new Vector3(Vector3.Dot(offset, Right), offset.Y, Vector3.Dot(offset, Forward));
    }

    /// <summary>A world yaw as degrees from facing the way the anchor does.</summary>
    public float ToLocalYaw(float worldYaw) => AngleHelpers.Wrap(worldYaw - Yaw);

    public float ToWorldYaw(float localYaw) => AngleHelpers.Wrap(localYaw + Yaw);

    /// <summary>World yaw for an actor spot's facing, with the anchor at your end of a lane: 0 looks back at you, positive to its right.</summary>
    public float ActorYaw(float facing) => ToWorldYaw(180f + facing);
}
