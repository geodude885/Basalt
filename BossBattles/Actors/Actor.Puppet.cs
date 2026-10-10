using BossBattles.Core;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Networking;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppRUMBLE.Poses;
using UnityEngine;
using UnityEngine.InputSystem.XR;

namespace BossBattles.Actors;

// PUPPET MODE - testing only, kept in this one file so it can be pulled out.
//
// A puppet is the ReplayMod way of making a clone: a Remote player with its networking stripped,
// posed entirely by this mod. The game's movement, physics states, moves and knockback do not run
// on it, which is why battles use Simulated actors. It stays for comparing behaviour.
//
// To remove puppet mode: delete this file, delete ActorMode.Puppet, delete the lines marked
// "// PUPPET" in Actor.cs, ActorPatches.cs and ModEntry.cs.
internal sealed partial class Actor
{
    private const float PuppetGravity = 9.81f;
    private const float PuppetGroundFriction = 4f;

    /// <summary>Puppet knockback speed per tier point, m/s - the game's knockback cannot move a puppet, so it is redone.</summary>
    public static float PuppetKnockbackScale { get; set; } = 1.5f;

    private Transform? _physics;
    private Vector3 _anchorPosition;
    private Quaternion _anchorRotation;
    private float _floorY;
    private Vector3 _knockVelocity;

    public bool IsPuppet => Exists && Mode == ActorMode.Puppet;

    private void SetupPuppet(PlayerController controller, GameObject body, Vector3 floorPosition, Quaternion facing)
    {
        Transform rig = ActorRig.Root(controller);

        DestroyIfPresent(rig.GetComponent<NetworkGameObject>());
        DestroyIfPresent(rig.GetComponent<PlayerSessionStateSystem>());
        DestroyIfPresent(rig.GetComponent<Rigidbody>());

        // From ReplayMod; the parameter behind the hash is not known.
        controller.PlayerAnimator.animator.SetBool(-414412114, false);

        Transform physics = body.transform.GetChild(3);
        LockJoint(physics.GetChild(2).GetComponent<ConfigurableJoint>());
        LockJoint(physics.GetChild(3).GetComponent<ConfigurableJoint>());

        foreach (TrackedPoseDriver driver in body.GetComponentsInChildren<TrackedPoseDriver>()) driver.enabled = false;

        PlayerPoseSystem poseSystem = body.GetComponent<PlayerPoseSystem>();
        foreach (PoseInputSource pose in PlayerManager.instance.localPlayer.Controller.PlayerPoseSystem.currentInputPoses)
            poseSystem.currentInputPoses.Add(new PoseInputSource(pose.PoseSet));
        poseSystem.enabled = true;

        // With the network object and rig rigidbody gone these throw every frame.
        DisableAll<PlayerNetworking>(body);
        DisableAll<PlayerPhysics>(body);

        // Nothing drives the physics root of a non-networked remote player: it free-falls. Kinematic,
        // and moved with the rig in UpdatePuppet.
        _physics = physics;
        Rigidbody? physicsBody = physics.GetComponent<Rigidbody>();
        physicsBody?.isKinematic = true;

        _anchorPosition = floorPosition;
        _anchorRotation = facing;
        _floorY = floorPosition.y;
        body.transform.SetPositionAndRotation(floorPosition, facing);
    }

    /// <summary>The puppet copies your pose in its own frame, facing you; a knockback slides it.</summary>
    private void UpdatePuppet()
    {
        Vector3 knocked = IntegratePuppetKnockback(Time.deltaTime);

        if (_knockedOut)
        {
            if (knocked != Vector3.zero && _rig != null)
            {
                _rig.Vr.position += knocked;
                _physics?.position += knocked;
            }

            return;
        }

        PlayerController? local = PlayerManager.instance?.localPlayer?.Controller;
        if (local == null || _rig == null) return;

        var source = ActorRig.Of(local);
        Quaternion toPuppet = _anchorRotation * Quaternion.Inverse(Quaternion.LookRotation(source.Facing));

        _rig.Vr.SetPositionAndRotation(
            _anchorPosition + (toPuppet * (source.Vr.position - source.FloorPoint)),
            toPuppet * source.Vr.rotation);

        Transform? sourcePhysics = local.transform.Find("Physics");
        if (_physics != null && sourcePhysics != null)
        {
            _physics.SetPositionAndRotation(
                _anchorPosition + (toPuppet * (sourcePhysics.position - source.FloorPoint)),
                toPuppet * sourcePhysics.rotation);
        }

        ActorRig.CopyLocal(source.Offset, _rig.Offset);
        ActorRig.CopyLocal(source.Head, _rig.Head);
        ActorRig.CopyLocal(source.LeftHand, _rig.LeftHand);
        ActorRig.CopyLocal(source.RightHand, _rig.RightHand);
    }

    /// <summary>The game's knockback cannot move a puppet (its body is kinematic), so it becomes a slide here.</summary>
    public void ApplyPuppetKnockback(Vector3 velocity)
    {
        if (!IsPuppet) return;
        _knockVelocity += velocity;
        Mod.Note("Actor.PuppetKnockback", $"{this}: +{velocity.magnitude:F2} m/s", echo: false);
    }

    private Vector3 IntegratePuppetKnockback(float deltaTime)
    {
        if (_knockVelocity == Vector3.zero || deltaTime <= 0f) return Vector3.zero;

        Vector3 velocity = _knockVelocity;
        velocity.y -= PuppetGravity * deltaTime;

        Vector3 next = _anchorPosition + (velocity * deltaTime);
        if (next.y <= _floorY)
        {
            next.y = _floorY;
            velocity.y = 0f;

            var keep = Mathf.Exp(-PuppetGroundFriction * deltaTime);
            velocity.x *= keep;
            velocity.z *= keep;
            if ((velocity.x * velocity.x) + (velocity.z * velocity.z) < 0.01f) velocity = Vector3.zero;
        }

        Vector3 delta = next - _anchorPosition;
        _anchorPosition = next;
        _knockVelocity = velocity;
        return delta;
    }

    private void ForgetPuppet()
    {
        _physics = null;
        _knockVelocity = Vector3.zero;
    }

    private static void LockJoint(ConfigurableJoint? joint)
    {
        if (joint == null) return;
        joint.xMotion = ConfigurableJointMotion.Locked;
        joint.yMotion = ConfigurableJointMotion.Locked;
        joint.zMotion = ConfigurableJointMotion.Locked;
    }

    private static void DestroyIfPresent(UnityEngine.Object? obj)
    {
        if (obj != null) UnityEngine.Object.Destroy(obj);
    }
}
