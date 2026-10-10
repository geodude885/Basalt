using System.Numerics;

namespace BossBattles.Services.Clips;

/// <summary>A local pose inside a player's rig: what a headset or controller reports.</summary>
public readonly record struct ClipPose(Vector3 Position, Quaternion Rotation)
{
    public static ClipPose Lerp(ClipPose a, ClipPose b, float t) =>
        new(Vector3.Lerp(a.Position, b.Position, t), Quaternion.Slerp(a.Rotation, b.Rotation, t));
}

/// <summary>
/// One sample of a performance: the head-and-hands poses inside the rig (with the headset offset,
/// which carries height calibration), the two sticks in the game's own units (-1..1), and - for a
/// take recorded in a lane - where the performer stood in it (<see cref="Lane"/>: lane x and z of
/// the floor under their head) and which way their body was turned (<see cref="Yaw"/>: degrees from
/// facing down the lane, clockwise from above), which an actor playing it back is kept to.
/// </summary>
public sealed record ClipFrame(
    float Time,
    ClipPose Offset,
    ClipPose Head,
    ClipPose LeftHand,
    ClipPose RightHand,
    Vector2 Walk,
    Vector2 Turn,
    Vector2? Lane = null,
    float? Yaw = null);

/// <summary>A move the game recognised from the performer, by its stack name, at a clip time.</summary>
public readonly record struct ClipMove(float Time, string Move);

/// <summary>
/// A recorded performance: what a player's tracked parts and sticks did over time. Played back on
/// an actor, it goes through the same pose and stick input the game reads from a headset and
/// controllers - so the game itself recognises the gestures, and the actor's moves are real ones.
/// Rig-local, so it plays anywhere and facing any way.
/// </summary>
public sealed record Clip(string Name, IReadOnlyList<ClipFrame> Frames)
{
    public float Duration => Frames.Count == 0 ? 0f : Frames[^1].Time;

    /// <summary>The moves made during it, in time order - a take replays these itself rather than trusting gestures.</summary>
    public IReadOnlyList<ClipMove> Moves { get; init; } = Array.Empty<ClipMove>();

    /// <summary>The time of its first frame - a take keeps chart time, so it may start late.</summary>
    public float Start => Frames.Count == 0 ? 0f : Frames[0].Time;

    /// <summary>
    /// The pose and sticks at <paramref name="time"/>: poses interpolated between the frames either
    /// side, sticks held from the earlier one (a stick is a press, not a motion). Clamped to the
    /// first and last frames.
    /// </summary>
    public ClipFrame Sample(float time)
    {
        if (Frames.Count == 0) throw new InvalidOperationException($"clip \"{Name}\" has no frames");
        if (time <= Frames[0].Time) return Frames[0];
        if (time >= Duration) return Frames[^1];

        var after = FirstAfter(time);
        ClipFrame a = Frames[after - 1];
        ClipFrame b = Frames[after];
        var t = (time - a.Time) / (b.Time - a.Time);

        return new ClipFrame(
            time,
            ClipPose.Lerp(a.Offset, b.Offset, t),
            ClipPose.Lerp(a.Head, b.Head, t),
            ClipPose.Lerp(a.LeftHand, b.LeftHand, t),
            ClipPose.Lerp(a.RightHand, b.RightHand, t),
            a.Walk,
            a.Turn,
            a.Lane is { } la && b.Lane is { } lb ? Vector2.Lerp(la, lb, t) : a.Lane ?? b.Lane,
            a.Yaw is { } ya && b.Yaw is { } yb ? LerpDegrees(ya, yb, t) : a.Yaw ?? b.Yaw);
    }

    /// <summary>Between two angles the short way round: 350 to 10 passes through 0, not 180.</summary>
    public static float LerpDegrees(float from, float to, float t)
    {
        var delta = ((((to - from) % 360f) + 540f) % 360f) - 180f;
        var value = (from + (delta * t)) % 360f;
        return value < 0f ? value + 360f : value;
    }

    /// <summary>Index of the first frame later than <paramref name="time"/> - binary search; frames are time-ordered.</summary>
    private int FirstAfter(float time)
    {
        var low = 0;
        var high = Frames.Count - 1;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (Frames[mid].Time <= time) low = mid + 1;
            else high = mid;
        }

        return low;
    }
}
