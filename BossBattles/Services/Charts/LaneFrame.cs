using System.Numerics;

namespace BossBattles.Services.Charts;

public readonly record struct LaneFrame(Anchor Anchor, float DepthScale = 1f)
{
    public static LaneFrame From(Vector3 origin, Vector3 facing, float depthScale = 1f) => new(Anchor.Facing(origin, facing), depthScale);

    public static LaneFrame Between(Vector3 near, Vector3 far, float chartLength)
    {
        var flat = new Vector3(far.X - near.X, 0f, far.Z - near.Z);
        var real = flat.Length();
        return From(near, flat, real > 0.01f && chartLength > 0f ? real / chartLength : 1f);
    }

    public Vector3 ToWorld(Vector3 lane) => Anchor.ToWorld(lane with { Z = lane.Z * DepthScale });

    public Vector3 ToLane(Vector3 world)
    {
        Vector3 local = Anchor.ToLocal(world);
        return local with { Z = local.Z / DepthScale };
    }

    public float ActorYawDegrees(float facing) => Anchor.ToWorldYaw(180f + facing);
}
