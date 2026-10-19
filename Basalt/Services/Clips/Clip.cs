using System.Numerics;
using Il2CppRUMBLE.Players.Subsystems;

namespace Basalt.Services.Clips;

/// <summary>A local pose inside a player's rig: what a headset or controller reports.</summary>
public readonly record struct ClipPose(Vector3 Position, Quaternion Rotation)
{

    public static ClipPose From(UnityEngine.Transform part) => new(
        new Vector3(part.localPosition.x, part.localPosition.y, part.localPosition.z),
        new Quaternion(part.localRotation.x, part.localRotation.y, part.localRotation.z, part.localRotation.w));

    public static ClipPose Lerp(ClipPose a, ClipPose b, float t) =>
        new(Vector3.Lerp(a.Position, b.Position, t), Quaternion.Slerp(a.Rotation, b.Rotation, t));
}

/// <summary>How a hand's fingers are held: the controller's index, grip, thumb and sway, 0..1, as the game animates them.</summary>
public readonly record struct ClipFingers(float Index, float Grip, float Thumb, float Sway)
{
    public static ClipFingers From(PlayerHandPresence.HandPresenceInput input) =>
        new(input.indexInput, input.gripInput, input.thumbInput, input.swayInput);

    public static ClipFingers Lerp(ClipFingers a, ClipFingers b, float t) => new(
        a.Index + ((b.Index - a.Index) * t),
        a.Grip + ((b.Grip - a.Grip) * t),
        a.Thumb + ((b.Thumb - a.Thumb) * t),
        a.Sway + ((b.Sway - a.Sway) * t));
}

/// <summary>
/// One sample of a performance: head and hands inside the rig, and for a clip recorded in a lane, where the
/// performer stood (<see cref="Offset"/>: x, z from the lane's start) and faced (<see cref="Yaw"/>: degrees from
/// down the lane, clockwise from above).
/// </summary>
public record ClipFrame(
    float Time,
    ClipPose Head,
    ClipPose LeftHand,
    ClipPose RightHand,
    Vector2? Offset = null,
    float? Yaw = null,
    ClipFingers? LeftFingers = null,
    ClipFingers? RightFingers = null);

/// <summary>A move the game recognised from the performer, by its stack name, at a clip time.</summary>
public readonly record struct ClipMove(float Time, string Move);

/// <summary>
/// A recorded performance, rig-local so it plays anywhere.
/// </summary>
public record Clip(string Name, IReadOnlyList<ClipFrame> Frames)
{
    public float Duration => Frames.Count == 0 ? 0f : Frames[^1].Time;

    /// <summary>The moves made during it, in time order - playback runs these itself rather than trusting gestures.</summary>
    public IReadOnlyList<ClipMove> Moves { get; init; } = Array.Empty<ClipMove>();

    /// <summary>The time of its first frame: 0, except in clips recorded before clips kept their own time.</summary>
    public float Start => Frames.Count == 0 ? 0f : Frames[0].Time;

    /// <summary>The frame at <paramref name="time"/>: poses, spot, facing and fingers interpolated.</summary>
    public ClipFrame Sample(float time)
    {
        if (Frames.Count == 0) throw new InvalidOperationException($"clip \"{Name}\" has no frames");
        if (time <= Frames[0].Time) return Frames[0];
        if (time >= Duration) return Frames[^1];

        var after = FirstAfter(time);
        var a = Frames[after - 1];
        var b = Frames[after];
        var t = (time - a.Time) / (b.Time - a.Time);

        return new ClipFrame(
            time,
            ClipPose.Lerp(a.Head, b.Head, t),
            ClipPose.Lerp(a.LeftHand, b.LeftHand, t),
            ClipPose.Lerp(a.RightHand, b.RightHand, t),
            a.Offset is { } oa && b.Offset is { } ob ? Vector2.Lerp(oa, ob, t) : a.Offset ?? b.Offset,
            a.Yaw is { } ya && b.Yaw is { } yb ? Degrees.Lerp(ya, yb, t) : a.Yaw ?? b.Yaw,
            a.LeftFingers is { } la && b.LeftFingers is { } lb ? ClipFingers.Lerp(la, lb, t) : a.LeftFingers ?? b.LeftFingers,
            a.RightFingers is { } ra && b.RightFingers is { } rb ? ClipFingers.Lerp(ra, rb, t) : a.RightFingers ?? b.RightFingers);
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
