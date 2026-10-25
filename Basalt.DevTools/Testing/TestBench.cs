using Basalt.Actors;
using Basalt.Battle;
using UnityEngine;

namespace Basalt.DevTools.Testing;

/// <summary>
/// A test actor outside any battle, 3 m in front of you. It mirrors you, or in bot control plain keys
/// drive it: WASD walk, Q/E turn, F face you, Space jump, Shift dash, 1-4 spawns, Z/X/C/V strikes, T disc, G explode.
/// </summary>
internal static class TestBench
{
    private const float SpawnDistance = 3f;
    private const float FacingCheckInterval = 0.25f;
    private const float FacingTolerance = 15f;
    private static readonly float[] KnockbackSteps = { 1f, 0.5f, 0.2f, 0f };

    private static readonly (KeyCode Key, string Stack)[] Moves =
    {
        (KeyCode.Space, "Jump"),
        (KeyCode.LeftShift, "RockSlide"),
        (KeyCode.Alpha1, "SpawnCube"),
        (KeyCode.Alpha2, "SpawnPillar"),
        (KeyCode.Alpha3, "SpawnWall"),
        (KeyCode.Alpha4, "SpawnBall"),
        (KeyCode.Z, "Straight"),
        (KeyCode.X, "Uppercut"),
        (KeyCode.C, "Kick"),
        (KeyCode.V, "Stomp"),
        (KeyCode.T, "Disc"),
        (KeyCode.G, "Explode"),
    };

    private static Actor? _actor;
    private static bool _mirroring;
    private static bool _facingPlayer;
    private static float _nextFacingCheck;

    public static Actor? Actor => _actor is { Ready: true } ? _actor : null;

    /// <summary>The test actor's sticks while it is bot-controlled; null while it mirrors you.</summary>
    public static (Vector2 Walk, Vector2 Turn)? Sticks { get; private set; }

    public static void ToggleActor()
    {
        if (Actor != null)
        {
            Mod.Actors.Despawn(_actor!);
            Forget();
            return;
        }

        var playerController = BattlePlayer.Player?.Controller;
        if (playerController == null) return;

        var playerRig = PlayerRig.From(playerController);
        var at = playerRig.Feet + (playerRig.Facing * SpawnDistance);

        _actor = new Actor("test", "Test Actor", "you");
        Mod.Actors.Spawn(_actor, at, Quaternion.LookRotation(-playerRig.Facing));
        _mirroring = true;
    }

    public static void ToggleControl()
    {
        if (Actor == null) return;

        _mirroring = !_mirroring;
        _facingPlayer = !_mirroring;
        Sticks = null;
        Logger.Info($"[Test.Control] {(_mirroring ? "mirroring you" : "bot control")}");
    }

    public static void TogglePlant()
    {
        if (Actor?.Body is not { } body) return;

        if (body.Footing.Planted) body.Footing.AllowFreePlacement();
        else body.Footing.KeepInPlace();
        Logger.Info(body.Footing.Planted ? "[Test.Plant] planted" : "[Test.Plant] unplanted");
    }

    public static void CycleKnockback()
    {
        if (Actor is not { } actor) return;

        var next = (Array.IndexOf(KnockbackSteps, actor.SelfKnockbackMultiplier) + 1) % KnockbackSteps.Length;
        actor.SelfKnockbackMultiplier = KnockbackSteps[next];
        Logger.Info($"[Test.Knockback] test actor takes x{actor.SelfKnockbackMultiplier} of the game's knockback");
    }

    /// <summary>Mirrors you, or reads the keys; plain keys drive the actor, Ctrl+ keys are the tools'.</summary>
    public static void Update()
    {
        if (Actor is not { } actor || BattlePlayer.Player?.Controller is not { } playerController) return;

        var playerRig = PlayerRig.From(playerController);
        if (_mirroring)
        {
            actor.Body?.Clips.RestingPose = ActorPose.Snapshot(playerRig);
            return;
        }

        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
        {
            Sticks = (Vector2.zero, Vector2.zero);
            return;
        }

        var turn = new Vector2(Axis(KeyCode.E, KeyCode.Q), 0f);
        Sticks = (new Vector2(Axis(KeyCode.D, KeyCode.A), Axis(KeyCode.W, KeyCode.S)), turn);

        if (turn != Vector2.zero) _facingPlayer = false;
        if (Input.GetKeyDown(KeyCode.F)) _facingPlayer = !_facingPlayer;

        foreach ((_, var stack) in Moves.Where(m => Input.GetKeyDown(m.Key)))
        {
            actor.RunMove(stack);
        }

        if (!_facingPlayer || Time.time < _nextFacingCheck || actor.Body is not { } body) return;
        _nextFacingCheck = Time.time + FacingCheckInterval;

        var rig = body.Rig;
        var toPlayer = playerRig.Head.position - rig.Head.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.01f) return;

        var angle = Vector3.SignedAngle(rig.Facing, toPlayer.normalized, Vector3.up);
        if (Mathf.Abs(angle) >= FacingTolerance) body.Controller.PlayerMovement.Rotate(angle, false);
    }

    /// <summary>For a scene change: the test actor's body went with the scene.</summary>
    public static void Forget()
    {
        _actor = null;
        Sticks = null;
        _facingPlayer = false;
    }

    private static float Axis(KeyCode positive, KeyCode negative) =>
        (Input.GetKey(positive) ? 1f : 0f) - (Input.GetKey(negative) ? 1f : 0f);
}
