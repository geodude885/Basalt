using System.Collections;
using Il2CppRUMBLE.Players;
using MelonLoader;
using UnityEngine;

namespace Basalt.Actors;

internal class ActorPlacement(Actor actor, PlayerController controller, PlayerRig rig)
{
    private const RigidbodyConstraints KeepInPlaceConstraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ;

    public bool Planted { get; private set; }

    public void Place(Vector3 floorPosition, float yawDegrees)
    {
        AllowFreePlacement();

        // Reposition places the head, not the feet.
        var eyeHeight = rig.Head.position.y - rig.Vr.position.y;
        controller.PlayerMovement.Reposition(floorPosition + (Vector3.up * eyeHeight), Quaternion.Euler(0f, yawDegrees, 0f));

        KeepInPlace();

        Logger.Debug($"[Actor.Placed] {actor} at {floorPosition.Describe()} facing {yawDegrees:F0}");
        MelonCoroutines.Start(PutOnFloor(floorPosition.y));
    }

    public void KeepInPlace()
    {
        Planted = true;
        KeepPlanted();
    }

    public void AllowFreePlacement()
    {
        Planted = false;
        foreach (var body in Bodies())
        {
            body?.constraints &= ~KeepInPlaceConstraints;
        }
    }

    public void Update()
    {
        if (Planted) KeepPlanted();
    }

    public void Shift(Vector3 offset)
    {
        foreach (var body in Bodies())
        {
            body?.position += offset;
        }
    }

    private void KeepPlanted()
    {
        foreach (var body in Bodies())
        {
            if (body == null || (body.constraints & KeepInPlaceConstraints) == KeepInPlaceConstraints) continue;
            body.constraints |= KeepInPlaceConstraints;
        }
    }

    private Rigidbody?[] Bodies()
    {
        var physics = controller?.PlayerPhysics;
        return physics == null ? Array.Empty<Rigidbody?>() : [physics.PhysicsRigidbody, physics.VRrigidbody];
    }

    private IEnumerator PutOnFloor(float floor)
    {
        foreach (var _ in Enumerable.Range(0, 2))
        {
            yield return new WaitForSeconds(0.08f);
            if (!actor.Ready || actor.Body.Footing != this) yield break;

            var above = rig.Feet.y - floor;
            if (above is < 0.05f or > 2.5f) yield break;

            foreach (var body in Bodies())
            {
                if (body == null) continue;
                body.position += Vector3.down * above;
                body.velocity = Vector3.zero;
            }

            Logger.Debug($"[Actor.PutOnFloor] {actor}: {above:0.00} m above the floor - set down");
        }
    }
}
