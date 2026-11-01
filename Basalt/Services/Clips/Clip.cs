using System.Numerics;
using Il2CppRUMBLE.Players.Subsystems;

namespace Basalt.Services.Clips;

public readonly record struct ClipPose(Vector3 Position, Quaternion Rotation)
{
    public static ClipPose From(UnityEngine.Transform part) => new(
        new Vector3(part.localPosition.x, part.localPosition.y, part.localPosition.z),
        new Quaternion(part.localRotation.x, part.localRotation.y, part.localRotation.z, part.localRotation.w));

    public static ClipPose Lerp(ClipPose a, ClipPose b, float t) =>
        new(Vector3.Lerp(a.Position, b.Position, t), Quaternion.Slerp(a.Rotation, b.Rotation, t));
}

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

public record ClipFrame(
    float Time,
    ClipPose Head,
    ClipPose LeftHand,
    ClipPose RightHand,
    Vector2? Offset = null,
    float? Yaw = null,
    ClipFingers? LeftFingers = null,
    ClipFingers? RightFingers = null);

public readonly record struct ClipMove(float Time, string Move);

public record Clip(string Name, IReadOnlyList<ClipFrame> Frames)
{
    public float Duration => Frames.Count == 0 ? 0f : Frames[^1].Time;

    public IReadOnlyList<ClipMove> Moves { get; init; } = Array.Empty<ClipMove>();

    public float Start => Frames.Count == 0 ? 0f : Frames[0].Time;

    public ClipFrame GetClipFrameAt(float time)
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
            a.Yaw is { } ya && b.Yaw is { } yb ? AngleHelpers.Lerp(ya, yb, t) : a.Yaw ?? b.Yaw,
            a.LeftFingers is { } la && b.LeftFingers is { } lb ? ClipFingers.Lerp(la, lb, t) : a.LeftFingers ?? b.LeftFingers,
            a.RightFingers is { } ra && b.RightFingers is { } rb ? ClipFingers.Lerp(ra, rb, t) : a.RightFingers ?? b.RightFingers);
    }

    private int FirstAfter(float time) // Manually implement a binary search, why the hell not
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
