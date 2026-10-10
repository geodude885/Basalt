using System.Collections;
using BossBattles.Core;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using MelonLoader;
using UnityEngine;

namespace BossBattles.Actors;

/// <summary>The actor's body in the lane: placing and planting it, collisions, health and knock-outs.</summary>
internal sealed partial class Actor
{
    private const float KnockOutSeconds = 3f;
    private const short MaxHealth = 20;
    private const float PlacementCheckSeconds = 0.5f;
    private const float PlacementTolerance = 0.5f;

    /// <summary>How far a structure must be from the actor before it can hit the actor again.</summary>
    private const float ClearOfActor = 1.6f;

    private const RigidbodyConstraints PlantConstraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ;

    private Collider[]? _ownColliders;
    private bool _planted;
    private bool _knockedOut;

    public int KnockOuts { get; private set; }

    public bool KnockedOut => Exists && _knockedOut;

    /// <summary>How much of the game's knockback it takes: 1 normal, 0 none.</summary>
    public float KnockbackTaken { get; set; } = 1f;

    public bool Planted => Exists && _planted;

    public IEnumerable<Collider> Colliders => Exists ? _player!.Controller.GetComponentsInChildren<Collider>(true) : Array.Empty<Collider>();

    /// <summary>Raised on each knock-out (lethal damage, which the actor survives).</summary>
    public event Action<Actor>? WasKnockedOut;

    /// <summary>
    /// Moves the actor with the game's own <c>PlayerMovement.Reposition</c> (what puts players on
    /// spawn points) and plants it there.
    /// </summary>
    public void Place(Vector3 floorPosition, float yawDegrees)
    {
        if (!Exists) return;

        Unplant();

        // Reposition places the head, not the feet.
        var eyeHeight = _rig == null ? 0f : _rig.Head.position.y - _rig.Vr.position.y;
        _player!.Controller.PlayerMovement.Reposition(floorPosition + (Vector3.up * eyeHeight), Quaternion.Euler(0f, yawDegrees, 0f));
        Plant();
        Mod.Note("Actor.Placed", $"{this} at {Describe.Vector(floorPosition)} facing {yawDegrees:F0}", echo: false);
        MelonCoroutines.Start(SetDown(_player, floorPosition.y));
        MelonCoroutines.Start(CheckPlacement(_player, floorPosition));
    }

    /// <summary>
    /// Freezes X and Z on the physics and VR rigidbodies: gravity and jumps still work, but nothing
    /// slides it. The game's Rotate sets FreezeAll and restores it, so the bits are put back each frame.
    /// </summary>
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
        foreach (Rigidbody? body in PlantedBodies())
        {
            body?.constraints &= ~PlantConstraints;
        }
    }

    /// <summary>
    /// Stops a structure colliding with the actor until it is clear of it - one launched from beside
    /// the actor would otherwise be deflected - or <paramref name="seconds"/> at the latest.
    /// </summary>
    public void IgnoreStructure(GameObject structure, float seconds)
    {
        if (!Exists) return;

        _ownColliders ??= _player!.Controller.GetComponentsInChildren<Collider>(true);
        Collider[] structureColliders = structure.GetComponentsInChildren<Collider>(true);

        foreach (Collider mine in _ownColliders)
        {
            foreach (Collider theirs in structureColliders) Physics.IgnoreCollision(mine, theirs, true);
        }

        MelonCoroutines.Start(RestoreCollisionWhenClear(_ownColliders, structureColliders, structure.transform, seconds));
    }

    /// <summary>
    /// The actor's stand-in for death: HP shows 0 and the pose holds, then it is back on full health.
    /// The game's own death handling never runs for it - for a stripped clone it crashed the process.
    /// </summary>
    public void KnockOut(string reason)
    {
        if (!Exists || _knockedOut) return;

        _knockedOut = true;
        KnockOuts++;

        PlayerHealth health = _player!.Controller.PlayerHealth;
        var previous = _player.Data.HealthPoints;
        _player.Data.HealthPoints = 0;
        health.SetHealthBarPercentage(0f, previous, true);

        Mod.Note("Actor.KnockedOut", $"{this} #{KnockOuts}: {reason}");
        WasKnockedOut?.Invoke(this);
        MelonCoroutines.Start(ReviveAfterKnockOut(_player));
    }

    /// <summary>Full health now: a new player starts low and fills up over a second or two.</summary>
    public void FullHealth()
    {
        if (!Exists || _knockedOut) return;

        var hp = _player!.Data.HealthPoints;
        if (hp >= MaxHealth) return;

        _player.Controller.PlayerHealth.SetHealth(MaxHealth, hp, false);
        Mod.Note("Actor.FullHealth", $"{this}: {hp} -> {MaxHealth} at GO", echo: false);
    }

    private void KeepPlanted()
    {
        foreach (Rigidbody? body in PlantedBodies())
        {
            if (body == null || (body.constraints & PlantConstraints) == PlantConstraints) continue;
            body.constraints |= PlantConstraints;
        }
    }

    private Rigidbody?[] PlantedBodies()
    {
        PlayerPhysics? physics = _player?.Controller?.PlayerPhysics;
        return physics == null ? Array.Empty<Rigidbody?>() : new[] { physics.PhysicsRigidbody, physics.VRrigidbody };
    }

    /// <summary>
    /// Sets a placed actor down onto the floor it was sent to: one whose pose was not yet applied
    /// came out up to a metre high. Measured against that floor, never a ray - in the Ring a ray found
    /// a surface under the floor and pulled an actor through it.
    /// </summary>
    private IEnumerator SetDown(Player player, float floor)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            // Real time, not frames: at a high frame rate physics has not moved the bodies two frames on.
            yield return new WaitForSeconds(0.08f);
            if (!Owns(player) || !Exists || _rig == null) yield break;

            var above = _rig.FloorPoint.y - floor;
            if (above is < 0.05f or > 2.5f) yield break;

            foreach (Rigidbody? body in PlantedBodies())
            {
                if (body == null) continue;
                body.position += Vector3.down * above;
                body.velocity = Vector3.zero;
            }

            Mod.Note("Actor.SetDown", $"{this}: {above:0.00} m above the floor - set down", echo: false);
        }
    }

    private IEnumerator CheckPlacement(Player player, Vector3 wanted)
    {
        yield return new WaitForSeconds(PlacementCheckSeconds);
        if (!Owns(player) || !Exists || _rig == null) yield break;

        // Only across and along the lane: on uneven ground the actor settles on the real floor.
        Vector3 actual = _rig.FloorPoint;
        var off = Vector2.Distance(new Vector2(actual.x, actual.z), new Vector2(wanted.x, wanted.z));
        var close = off <= PlacementTolerance && actual.y > wanted.y - 2f;
        Mod.Note(close ? "Actor.PlacementOk" : "Actor.PlacementOff",
            $"{this}: sent to {Describe.Vector(wanted)}, stands at {Describe.Vector(actual)} ({off:F2} m off, {actual.y - wanted.y:+0.00;-0.00} m in height)", echo: !close);
    }

    private IEnumerator RestoreCollisionWhenClear(Collider[] mine, Collider[] theirs, Transform structure, float seconds)
    {
        var until = Time.time + seconds;
        while (Time.time < until && structure != null && Exists)
        {
            Vector3 body = _player!.Controller.transform.position;
            var apart = new Vector2(structure.position.x - body.x, structure.position.z - body.z);
            if (apart.magnitude > ClearOfActor) break;
            yield return null;
        }

        foreach (Collider a in mine)
        {
            if (a == null) continue;
            foreach (Collider b in theirs)
            {
                if (b != null) Physics.IgnoreCollision(a, b, false);
            }
        }
    }

    private IEnumerator ReviveAfterKnockOut(Player player)
    {
        yield return new WaitForSeconds(KnockOutSeconds);
        if (!Owns(player) || !Exists) yield break;

        try
        {
            player.Controller.PlayerHealth.SetHealth(MaxHealth, player.Data.HealthPoints, true);
        }
        catch (Exception e)
        {
            Mod.Note("Actor.ReviveFailed", $"{this}: {e}");
        }
        finally
        {
            _knockedOut = false;
        }
    }
}
