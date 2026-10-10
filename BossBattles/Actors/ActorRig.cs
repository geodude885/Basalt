using BossBattles.Core;
using Il2CppRUMBLE.Players;
using UnityEngine;

namespace BossBattles.Actors;

/// <summary>The tracked part of a player: the VR rig, which the body follows, and what hangs off it.</summary>
internal sealed record ActorRig(Transform Vr, Transform Offset, Transform Head, Transform LeftHand, Transform RightHand)
{
    private static readonly HashSet<string> ReportedFallbacks = new(StringComparer.Ordinal);

    // Names as logged from a live player in the Gym (VR/"Headset Offset"/Headset, "Left Controller",
    // "Right Controller"), with ReplayMod's indices as the fallback.
    public static ActorRig Of(PlayerController controller)
    {
        Transform vr = Root(controller);
        Transform offset = FindOr(vr, "Headset Offset", () => vr.GetChild(0));
        return new ActorRig(
            vr,
            offset,
            FindOr(offset, "Headset", () => offset.GetChild(0)),
            FindOr(vr, "Left Controller", () => vr.GetChild(1)),
            FindOr(vr, "Right Controller", () => vr.GetChild(2)));
    }

    public static Transform Root(PlayerController controller) =>
        FindOr(controller.transform, "VR", () => controller.transform.GetChild(2));

    /// <summary>The floor under the head: the head's x/z at the VR rig's height (the rig sits on the floor).</summary>
    public Vector3 FloorPoint => new(Head.position.x, Vr.position.y, Head.position.z);

    /// <summary>Where the head looks, flattened: the player's facing.</summary>
    public Vector3 Facing => Flat(Head.forward);

    public static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude < 1e-4f ? Vector3.forward : v.normalized;
    }

    public static void CopyLocal(Transform source, Transform target) =>
        target.SetLocalPositionAndRotation(source.localPosition, source.localRotation);

    private static Transform FindOr(Transform parent, string path, Func<Transform> fallback)
    {
        Transform found = parent.Find(path);
        if (found != null) return found;

        Transform byIndex = fallback();
        if (ReportedFallbacks.Add(path)) Mod.Note("Actor.RigByIndex", $"{parent.name}/{path} not found, using index: {byIndex.name}");
        return byIndex;
    }
}

/// <summary>A held head-and-hands pose, as local poses inside a rig - what an actor's "headset" and "controllers" report.</summary>
internal sealed record ActorPose(
    (Vector3 Position, Quaternion Rotation) Offset,
    (Vector3 Position, Quaternion Rotation) Head,
    (Vector3 Position, Quaternion Rotation) LeftHand,
    (Vector3 Position, Quaternion Rotation) RightHand)
{
    /// <summary>Your current pose, as it is right now.</summary>
    public static ActorPose Snapshot(ActorRig rig) => new(
        (rig.Offset.localPosition, rig.Offset.localRotation),
        (rig.Head.localPosition, rig.Head.localRotation),
        (rig.LeftHand.localPosition, rig.LeftHand.localRotation),
        (rig.RightHand.localPosition, rig.RightHand.localRotation));

    /// <summary>
    /// Standing upright, looking straight down the rig's forward, hands up in front - a fighter's
    /// guard - at your height (from your rig), so the body proportions look right.
    /// </summary>
    public static ActorPose Guard(ActorRig yours)
    {
        var eye = yours.Head.localPosition.y;
        if (eye < 1f) eye = 1.6f;

        return new ActorPose(
            (yours.Offset.localPosition, yours.Offset.localRotation),
            (new Vector3(0f, eye, 0f), Quaternion.identity),
            (new Vector3(-0.22f, eye - 0.45f, 0.28f), Quaternion.identity),
            (new Vector3(0.22f, eye - 0.45f, 0.28f), Quaternion.identity));
    }

    public void ApplyTo(ActorRig rig)
    {
        rig.Offset.SetLocalPositionAndRotation(Offset.Position, Offset.Rotation);
        rig.Head.SetLocalPositionAndRotation(Head.Position, Head.Rotation);
        rig.LeftHand.SetLocalPositionAndRotation(LeftHand.Position, LeftHand.Rotation);
        rig.RightHand.SetLocalPositionAndRotation(RightHand.Position, RightHand.Rotation);
    }
}
