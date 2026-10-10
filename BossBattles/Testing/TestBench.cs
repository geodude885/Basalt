using BossBattles.Actors;
using BossBattles.Core;
using BossBattles.Services.Clips;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;
using UnityEngine;

namespace BossBattles.Testing;

/// <summary>
/// A single test actor outside any battle, driven from the desktop keyboard - for trying the
/// actor's behaviour, moves and control layers by hand. Ctrl+T spawns or despawns it 3 m in front
/// of you; Ctrl+Shift+T makes it a puppet instead.
/// <list type="bullet">
/// <item>Ctrl+9 mirror (copies you) / bot (holds a guard pose, keys drive it).</item>
/// <item>Ctrl+P plant / unplant. Ctrl+K knockback taken 1 / 0.5 / 0.2 / 0.</item>
/// <item>In bot control, plain keys: WASD walk, Q/E turn, F face-you, Space jump, Left Shift dash,
/// 1-4 spawn cube/pillar/wall/ball, Z/X/C/V straight/uppercut/kick/stomp, T disc, G explode.</item>
/// </list>
/// </summary>
internal static class TestBench
{
    private const float SpawnDistance = 3f;
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

    public static Actor? Actor => _actor is { Exists: true } ? _actor : null;

    public static void ToggleActor(bool puppet)
    {
        if (Actor != null)
        {
            _actor!.Despawn();
            _actor = null;
            return;
        }

        PlayerController? you = PlayerManager.instance?.localPlayer?.Controller;
        if (you == null) return;

        var yours = ActorRig.Of(you);
        Vector3 at = yours.FloorPoint + (yours.Facing * SpawnDistance);

        _actor = new Actor("test", "Test Actor", "you", puppet ? ActorMode.Puppet : ActorMode.Simulated); // PUPPET (the mode choice)
        _actor.Spawn(at, Quaternion.LookRotation(-yours.Facing));
        _actor.SetControl(ActorControl.Mirror);
    }

    public static void ToggleControl()
    {
        if (Actor is not { IsSimulated: true } actor) return;

        if (actor.Control == ActorControl.Mirror)
        {
            PlayerController? you = PlayerManager.instance?.localPlayer?.Controller;
            if (you != null) actor.SetPose(ActorPose.Snapshot(ActorRig.Of(you)));
            actor.SetControl(ActorControl.Bot);
            actor.Input.FaceYou = true;
        }
        else
        {
            actor.SetControl(ActorControl.Mirror);
        }
    }

    public static void TogglePlant()
    {
        if (Actor is not { IsSimulated: true } actor) return;

        if (actor.Planted) actor.Unplant();
        else actor.Plant();
        Mod.Note("Test.Plant", actor.Planted ? "planted" : "unplanted");
    }

    public static void CycleKnockback()
    {
        if (Actor is not { } actor) return;

        var next = (Array.IndexOf(KnockbackSteps, actor.KnockbackTaken) + 1) % KnockbackSteps.Length;
        actor.KnockbackTaken = KnockbackSteps[next];
        Mod.Note("Test.Knockback", $"test actor takes x{actor.KnockbackTaken} of the game's knockback");
    }

    /// <summary>Plays a clip on the test actor, switching it to bot control first.</summary>
    public static void PlayClip(Clip? clip)
    {
        if (clip == null)
        {
            Mod.Say("[Clips] no clips yet - Ctrl+Shift+R records one");
            return;
        }

        if (Actor is not { IsSimulated: true } actor)
        {
            Mod.Say("[Clips] spawn a test actor first (Ctrl+T)");
            return;
        }

        if (actor.Control != ActorControl.Bot) ToggleControl();
        actor.Input.FaceYou = false;
        actor.PlayClip(clip);
        Mod.Say($"[Clips] test actor playing {clip.Name} ({clip.Duration:0.0}s)");
    }

    /// <summary>Plain keys drive the test actor while it is bot-controlled; Ctrl+ keys are the mod's.</summary>
    public static void Update()
    {
        if (Actor is not { IsBotControlled: true } actor) return;
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) return;

        actor.Input.Walk = new Vector2(Axis(KeyCode.D, KeyCode.A), Axis(KeyCode.W, KeyCode.S));
        actor.Input.Turn = new Vector2(Axis(KeyCode.E, KeyCode.Q), 0f);

        // Turning by hand and auto-facing would fight each other.
        if (actor.Input.Turn != Vector2.zero) actor.Input.FaceYou = false;
        if (Input.GetKeyDown(KeyCode.F)) actor.Input.FaceYou = !actor.Input.FaceYou;

        foreach ((KeyCode key, var stack) in Moves)
        {
            if (Input.GetKeyDown(key)) actor.RunMove(stack);
        }
    }

    /// <summary>For a scene change: the test actor's body went with the scene.</summary>
    public static void Forget() => _actor = null;

    private static float Axis(KeyCode positive, KeyCode negative) =>
        (Input.GetKey(positive) ? 1f : 0f) - (Input.GetKey(negative) ? 1f : 0f);
}
