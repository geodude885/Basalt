using System.Numerics;

namespace BossBattles.Services.Charts;

/// <summary>
/// A lane in the world: <see cref="Origin"/> is the centre of your end on the floor, and it runs
/// along <see cref="Forward"/>. <see cref="DepthScale"/> fits a chart's length to a real one by
/// scaling z only. Unity is left-handed: right = up x forward.
/// </summary>
public readonly record struct LaneFrame(Vector3 Origin, Vector3 Forward, float DepthScale = 1f)
{
    /// <summary>A frame from your floor position and the direction you face; the facing's tilt is ignored.</summary>
    public static LaneFrame From(Vector3 origin, Vector3 facing, float depthScale = 1f)
    {
        var flat = new Vector3(facing.X, 0f, facing.Z);
        if (flat.LengthSquared() < 1e-6f) flat = Vector3.UnitZ;
        return new LaneFrame(origin, Vector3.Normalize(flat), depthScale);
    }

    /// <summary>A frame from <paramref name="near"/> to <paramref name="far"/>, scaled to fit <paramref name="chartLength"/> between them.</summary>
    public static LaneFrame Between(Vector3 near, Vector3 far, float chartLength)
    {
        var flat = new Vector3(far.X - near.X, 0f, far.Z - near.Z);
        var real = flat.Length();
        return From(near, flat, real > 0.01f && chartLength > 0f ? real / chartLength : 1f);
    }

    public Vector3 Right => Vector3.Cross(Vector3.UnitY, Forward);

    public Vector3 ToWorld(Vector3 lane) =>
        Origin + (Right * lane.X) + (Vector3.UnitY * lane.Y) + (Forward * (lane.Z * DepthScale));

    public Vector3 ToLane(Vector3 world)
    {
        Vector3 offset = world - Origin;
        return new Vector3(Vector3.Dot(offset, Right), offset.Y, Vector3.Dot(offset, Forward) / DepthScale);
    }

    /// <summary>The compass bearing the lane runs, in degrees (Unity: 0 = +Z, clockwise from above).</summary>
    public float Bearing => Normalise(MathF.Atan2(Forward.X, Forward.Z) * 180f / MathF.PI);

    /// <summary>A world yaw as degrees from facing down the lane.</summary>
    public float ToLaneYaw(float worldYaw) => Normalise(worldYaw - Bearing);

    /// <summary>A yaw relative to the lane as a world yaw.</summary>
    public float ToWorldYaw(float laneYaw) => Normalise(laneYaw + Bearing);

    /// <summary>World yaw in degrees (Unity: 0 = +Z, clockwise from above) for an actor spot's facing.</summary>
    public float ActorYawDegrees(float facing)
    {
        var laneYaw = MathF.Atan2(Forward.X, Forward.Z) * 180f / MathF.PI;
        return Normalise(laneYaw + 180f + facing);
    }

    private static float Normalise(float degrees)
    {
        degrees %= 360f;
        return degrees < 0f ? degrees + 360f : degrees;
    }
}
