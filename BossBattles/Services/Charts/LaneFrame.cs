using System.Numerics;

namespace BossBattles.Services.Charts;

/// <summary>
/// Places a lane in the world: <see cref="Origin"/> is the centre of your end at floor height, and
/// the lane runs along <see cref="Forward"/> (horizontal) towards the far end. Lane x is to the
/// right looking down the lane, y is up. <see cref="DepthScale"/> fits a chart's lane length to a
/// shorter or longer real one - an arena's two player spots - by scaling z only.
/// <para>
/// System.Numerics, so it stays testable without the game; the game side converts to and from
/// Unity's Vector3. Unity is left-handed: right = up x forward.
/// </para>
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

    /// <summary>
    /// A frame running from <paramref name="near"/> (your end) to <paramref name="far"/>, scaled so a
    /// chart lane of <paramref name="chartLength"/> fits exactly between them.
    /// </summary>
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
