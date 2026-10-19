using BossBattles.Actors;
using BossBattles.Battle;
using UnityEngine;

namespace BossBattles.DevTools.Testing;

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
    private static bool _facingYou;
    private static float _nextFacingCheck;

    public static Actor? Actor => _actor?.Controller != null ? _actor : null;

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

        var you = BattlePlayer.Controller;
        if (you == null) return;

        var yours = ActorRig.From(you);
        var at = yours.Feet + (yours.Facing * SpawnDistance);

        _actor = new Actor("test", "Test Actor", "you");
        Mod.Actors.Spawn(_actor, at, Quaternion.LookRotation(-yours.Facing));
        _mirroring = true;
    }

    public static void ToggleControl()
    {
        if (Actor == null) return;

        _mirroring = !_mirroring;
        _facingYou = !_mirroring;
        Sticks = null;
        Logger.Info($"[Test.Control] {(_mirroring ? "mirroring you" : "bot control")}");
    }

    public static void TogglePlant()
    {
        if (Actor is not { } actor) return;

        if (actor.IsKeptInPlace) actor.AllowFreePlacement();
        else actor.KeepInPlace();
        Logger.Info(actor.IsKeptInPlace ? "[Test.Plant] planted" : "[Test.Plant] unplanted");
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
        if (Actor is not { } actor || BattlePlayer.Controller is not { } you) return;

        var yours = ActorRig.From(you);
        if (_mirroring)
        {
            actor.SetPose(ActorPose.Snapshot(yours));
            return;
        }

        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
        {
            Sticks = (Vector2.zero, Vector2.zero);
            return;
        }

        var turn = new Vector2(Axis(KeyCode.E, KeyCode.Q), 0f);
        Sticks = (new Vector2(Axis(KeyCode.D, KeyCode.A), Axis(KeyCode.W, KeyCode.S)), turn);

        if (turn != Vector2.zero) _facingYou = false;
        if (Input.GetKeyDown(KeyCode.F)) _facingYou = !_facingYou;

        foreach ((_, var stack) in Moves.Where(m => Input.GetKeyDown(m.Key)))
        {
            actor.RunMove(stack);
        }

        if (!_facingYou || Time.time < _nextFacingCheck || actor.Player is not { } body) return;
        _nextFacingCheck = Time.time + FacingCheckInterval;

        var rig = ActorRig.From(body.Controller);
        var toYou = yours.Head.position - rig.Head.position;
        toYou.y = 0f;
        if (toYou.sqrMagnitude < 0.01f) return;

        var angle = Vector3.SignedAngle(rig.Facing, toYou.normalized, Vector3.up);
        if (Mathf.Abs(angle) >= FacingTolerance) body.Controller.PlayerMovement.Rotate(angle, false);
    }

    /// <summary>For a scene change: the test actor's body went with the scene.</summary>
    public static void Forget()
    {
        _actor = null;
        Sticks = null;
        _facingYou = false;
    }

    private static float Axis(KeyCode positive, KeyCode negative) =>
        (Input.GetKey(positive) ? 1f : 0f) - (Input.GetKey(negative) ? 1f : 0f);
}
