using Il2CppRUMBLE.Players;
using UnityEngine;

namespace BossBattles.Actors;

// Just the colliders on an actor
internal record ActorRig(Transform Vr, Transform Head, Transform LeftHand, Transform RightHand)
{
    public static ActorRig From(PlayerController controller) =>
        new(controller.PlayerVR.Origin,
            controller.PlayerVR.Headset.Transform,
            controller.PlayerVR.LeftController.Transform,
            controller.PlayerVR.RightController.Transform);

    public Vector3 Feet => new(Head.position.x, Vr.position.y, Head.position.z);

    public Vector3 Facing => Head.forward.Flat();
}

internal record ActorPose(
    (Vector3 Position, Quaternion Rotation) Head,
    (Vector3 Position, Quaternion Rotation) LeftHand,
    (Vector3 Position, Quaternion Rotation) RightHand)
{
    public static ActorPose Snapshot(ActorRig rig) => new(
        (rig.Head.localPosition, rig.Head.localRotation),
        (rig.LeftHand.localPosition, rig.LeftHand.localRotation),
        (rig.RightHand.localPosition, rig.RightHand.localRotation));

    public static ActorPose DefaultPose(ActorRig yours)
    {
        var eye = yours.Head.localPosition.y;
        if (eye < 1f) eye = 1.6f;

        return new ActorPose(
            (new Vector3(0f, eye, 0f), Quaternion.identity),
            (new Vector3(-0.22f, eye - 0.45f, 0.28f), Quaternion.identity),
            (new Vector3(0.22f, eye - 0.45f, 0.28f), Quaternion.identity));
    }

    public void ApplyTo(ActorRig rig)
    {
        rig.Head.SetLocalPositionAndRotation(Head.Position, Head.Rotation);
        rig.LeftHand.SetLocalPositionAndRotation(LeftHand.Position, LeftHand.Rotation);
        rig.RightHand.SetLocalPositionAndRotation(RightHand.Position, RightHand.Rotation);
    }
}
