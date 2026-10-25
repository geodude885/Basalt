using Il2CppRUMBLE.Players;
using UnityEngine;

namespace Basalt;

// A player's VR rig - its origin, headset and controllers - yours or an actor's.
internal record PlayerRig(Transform Vr, Transform Head, Transform LeftHand, Transform RightHand)
{
    public static PlayerRig From(PlayerController controller) =>
        new(controller.PlayerVR.Origin,
            controller.PlayerVR.Headset.Transform,
            controller.PlayerVR.LeftController.Transform,
            controller.PlayerVR.RightController.Transform);

    public Vector3 Feet => new(Head.position.x, Vr.position.y, Head.position.z);

    public Vector3 Facing => Head.forward.Flat();
}
