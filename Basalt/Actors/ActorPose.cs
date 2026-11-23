using UnityEngine;

namespace Basalt.Actors;

internal record ActorPose(
    (Vector3 Position, Quaternion Rotation) Head,
    (Vector3 Position, Quaternion Rotation) LeftHand,
    (Vector3 Position, Quaternion Rotation) RightHand)
{
    public static ActorPose Snapshot(PlayerRig rig) => new(
        (rig.Head.localPosition, rig.Head.localRotation),
        (rig.LeftHand.localPosition, rig.LeftHand.localRotation),
        (rig.RightHand.localPosition, rig.RightHand.localRotation));

    private const float FallbackEyeHeight = 1.6f;

    public static ActorPose DefaultPose(float measuredHeight)
    {
        var eye = measuredHeight >= 1f ? measuredHeight : FallbackEyeHeight;

        return new ActorPose(
            (new Vector3(0f, eye, 0f), Quaternion.identity),
            (new Vector3(-0.22f, eye - 0.45f, 0.28f), Quaternion.identity),
            (new Vector3(0.22f, eye - 0.45f, 0.28f), Quaternion.identity));
    }

    public void ApplyTo(PlayerRig rig)
    {
        rig.Head.SetLocalPositionAndRotation(Head.Position, Head.Rotation);
        rig.LeftHand.SetLocalPositionAndRotation(LeftHand.Position, LeftHand.Rotation);
        rig.RightHand.SetLocalPositionAndRotation(RightHand.Position, RightHand.Rotation);
    }
}
