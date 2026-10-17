using System.Collections;
using Il2CppRUMBLE.Players;
using MelonLoader;
using UnityEngine;

namespace BossBattles.Actors;

internal partial class Actor
{
    public const short MaxHealth = 20;


    // How far a structure must be from the actor before it can hit the actor.
    private const float ClearOfActor = 1.6f;

    private const RigidbodyConstraints PlantConstraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ;

    private Collider[]? _colliders;
    private bool _planted;

    public float SelfKnockbackMultiplier { get; set; } = 1f;

    public bool Planted => Exists && _planted;

    public IEnumerable<Collider> Colliders => Exists ? _player!.Controller.GetComponentsInChildren<Collider>(true) : Array.Empty<Collider>();

    public void Place(Vector3 floorPosition, float yawDegrees)
    {
        if (!Exists) return;

        Unplant();

        // Reposition places the head, not the feet.
        var eyeHeight = _rig == null ? 0f : _rig.Head.position.y - _rig.Vr.position.y;
        _player!.Controller.PlayerMovement.Reposition(floorPosition + (Vector3.up * eyeHeight), Quaternion.Euler(0f, yawDegrees, 0f));
        Plant();
        Logger.Debug($"[Actor.Placed] {this} at {floorPosition.Describe()} facing {yawDegrees:F0}");
        MelonCoroutines.Start(PutOnFloor(_player, floorPosition.y));
    }

    public void Plant()
    {
        if (!Exists) return;
        _planted = true;
        KeepPlanted();
    }

    public void Unplant()
    {
        if (!Exists) return;

        _planted = false;
        foreach (var body in PlantedBodies())
        {
            body?.constraints &= ~PlantConstraints;
        }
    }

    // Stops a structure colliding with the actor until it is clear of it.
    public void IgnoreStructure(GameObject structure, float seconds)
    {
        if (!Exists) return;

        _colliders ??= _player!.Controller.GetComponentsInChildren<Collider>(true);
        Collider[] structureColliders = structure.GetComponentsInChildren<Collider>(true);

        foreach (var mine in _colliders)
        {
            foreach (var theirs in structureColliders) Physics.IgnoreCollision(mine, theirs, true);
        }

        MelonCoroutines.Start(RestoreCollisionWhenClear(_colliders, structureColliders, structure.transform, seconds));
    }

    public void FullHeal()
    {
        if (!Exists) return;

        var hp = _player!.Data.HealthPoints;
        if (hp >= MaxHealth) return;

        _player.Controller.PlayerHealth.SetHealth(MaxHealth, hp, false);
        Logger.Debug($"[Actor.FullHealth] {this}: {hp} -> {MaxHealth}");
    }

    private void KeepPlanted()
    {
        foreach (var body in PlantedBodies())
        {
            if (body == null || (body.constraints & PlantConstraints) == PlantConstraints) continue;
            body.constraints |= PlantConstraints;
        }
    }

    private Rigidbody?[] PlantedBodies()
    {
        var physics = _player?.Controller?.PlayerPhysics;
        return physics == null ? Array.Empty<Rigidbody?>() : new[] { physics.PhysicsRigidbody, physics.VRrigidbody };
    }

    private IEnumerator PutOnFloor(Player player, float floor)
    {
        foreach (var _ in Enumerable.Range(0, 2))
        {
            yield return new WaitForSeconds(0.08f);
            if (!Owns(player) || !Exists || _rig == null) yield break;

            var above = _rig.Feet.y - floor;
            if (above is < 0.05f or > 2.5f) yield break;

            foreach (var body in PlantedBodies())
            {
                if (body == null) continue;
                body.position += Vector3.down * above;
                body.velocity = Vector3.zero;
            }

            Logger.Debug($"[Actor.PutOnFloor] {this}: {above:0.00} m above the floor - set down");
        }
    }

    private IEnumerator RestoreCollisionWhenClear(Collider[] mine, Collider[] theirs, Transform structure, float seconds)
    {
        var until = Time.time + seconds;
        while (Time.time < until && structure != null && Exists)
        {
            var body = _player!.Controller.transform.position;
            var apart = new Vector2(structure.position.x - body.x, structure.position.z - body.z);
            if (apart.magnitude > ClearOfActor) break;
            yield return null;
        }

        foreach (var a in mine)
        {
            if (a == null) continue;
            foreach (var b in theirs)
            {
                if (b != null) Physics.IgnoreCollision(a, b, false);
            }
        }
    }
}
