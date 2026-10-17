using System.Collections;
using BossBattles.Actors;
using BossBattles.Services.Charts;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using MelonLoader;
using UnityEngine;

namespace BossBattles.Battle;

/// <summary>You in a battle: placed in the lane, kept inside it, open to the game's hits, and on full health at either end.</summary>
internal class BattlePlayer
{
    private PlayerHealth? _invincibleBefore;
    private float _nextLaneCheck;
    private int _placement;

    public static Player? Player => PlayerManager.instance?.localPlayer;

    public static PlayerController? Controller => Player?.Controller;

    public static ActorRig? Rig => Controller is { } you ? ActorRig.From(you) : null;

    public static Vector3 Head => Rig?.Head.position ?? Vector3.zero;

    /// <summary>
    /// Moves you with the game's <c>Reposition</c>, then corrects once from where your feet landed:
    /// it places the head, and turning you swings the head's offset from the rig with it.
    /// </summary>
    public void Place(Vector3 floor, Quaternion facing)
    {
        if (Controller is not { } you) return;

        var rig = ActorRig.From(you);
        var eyeHeight = rig.Head.position.y - rig.Vr.position.y;
        you.PlayerMovement.Reposition(floor + (Vector3.up * eyeHeight), facing);
        MelonCoroutines.Start(CorrectPlacement(you, floor, facing, eyeHeight, ++_placement));
    }

    /// <summary>
    /// The walls' safety net: past one anyway, you are pushed back inside the lane - both bodies
    /// shifted, height kept, and the speed carrying you out stopped. Without <paramref name="zoned"/> the whole lane is yours.
    /// </summary>
    public void KeepInLane(Anchor frame, Chart chart, bool zoned)
    {
        if (Time.time < _nextLaneCheck || Controller is not { } you) return;
        _nextLaneCheck = Time.time + 0.1f;

        var rig = ActorRig.From(you);
        Vector3 at = frame.ToLocal(rig.Feet.ToNumerics()).ToUnity();
        float side = (chart.Lane.Width / 2f) + 0.3f, back = -0.75f;
        var far = zoned ? chart.Zone.Length + 0.3f : chart.Lane.Length + 0.75f;
        var above = rig.Vr.position.y - frame.Position.Y;
        var tooHigh = zoned && above > chart.Zone.Height + 0.8f;
        if (!tooHigh && Math.Abs(at.x) <= side + 0.3f && at.z >= back - 0.3f && at.z <= far + 0.3f) return;

        Vector3 inside = frame.ToWorld(new System.Numerics.Vector3(Math.Clamp(at.x, -side + 0.2f, side - 0.2f), 0f, Math.Clamp(at.z, back + 0.2f, far - 0.2f))).ToUnity();
        Vector3 floor = rig.Feet;
        var shift = new Vector3(inside.x - floor.x, 0f, inside.z - floor.z);
        if (tooHigh) shift.y = -(above - chart.Zone.Height);
        PlayerPhysics physics = you.PlayerPhysics;
        foreach (Rigidbody? body in new[] { physics.PhysicsRigidbody, physics.VRrigidbody })
        {
            if (body == null) continue;
            body.position += shift;
            Vector3 v = body.linearVelocity;
            Vector3 outward = shift.sqrMagnitude > 1e-6f ? -shift.normalized : Vector3.zero;
            var away = Vector3.Dot(v, outward);
            if (away > 0f) body.linearVelocity = v - (outward * away);
        }

        Logger.Debug($"[Battle.YouLeftTheLane] at lane x {at.x:0.0}, z {at.z:0.0}{(tooHigh ? $", {above:0.0} m up" : "")} - put back");
    }

    /// <summary>
    /// Outside a match the Ring and Pit make you invincible and the game regenerates your health:
    /// both are held off while running, so the game's own hits count.
    /// </summary>
    public void KeepVulnerable()
    {
        if (Controller is not { } you) return;

        try
        {
            PlayerHealth health = you.PlayerHealth;
            if (health.IsInvincible)
            {
                _invincibleBefore ??= health;
                health.IsInvincible = false;
                Logger.Debug("[Battle.InvincibilityLifted] you were invincible here - lifted for the run");
            }

            if (health.currentHealthResetRoutine != null)
            {
                health.StopCoroutine(health.currentHealthResetRoutine);
                health.currentHealthResetRoutine = null;
                Logger.Debug("[Battle.RegenHeld] the game's health regeneration stopped for the run");
            }
        }
        catch (Exception e)
        {
            Logger.Error($"[Battle.VulnerableFailed] {e}");
        }
    }

    /// <summary>Your invincibility back, if the arena had made you so.</summary>
    public void RestoreInvincibility()
    {
        if (_invincibleBefore == null) return;

        try
        {
            _invincibleBefore.IsInvincible = true;
        }
        catch (Exception e)
        {
            Logger.Error($"[Battle.VulnerableFailed] {e.Message}");
        }

        _invincibleBefore = null;
    }

    public static void FullHealth()
    {
        Player? you = Player;
        if (you?.Controller == null) return;

        var hp = you.Data.HealthPoints;
        if (hp < PlayerHealth.MAX_PLAYER_HEALTH) you.Controller.PlayerHealth.SetHealth((short)PlayerHealth.MAX_PLAYER_HEALTH, hp, false);
        Logger.Debug($"[Battle.FullHealth] {hp} -> {PlayerHealth.MAX_PLAYER_HEALTH}");
    }

    private IEnumerator CorrectPlacement(PlayerController you, Vector3 floor, Quaternion facing, float eyeHeight, int placement)
    {
        yield return null;
        yield return null;
        if (placement != _placement || you == null) yield break;

        Vector3 feet = ActorRig.From(you).Feet;
        var off = new Vector3(floor.x - feet.x, 0f, floor.z - feet.z);
        if (off.magnitude > 0.15f)
        {
            you.PlayerMovement.Reposition(floor + off + (Vector3.up * eyeHeight), facing);
            yield return null;
            yield return null;
            if (placement != _placement || you == null) yield break;
            feet = ActorRig.From(you).Feet;
        }

        var left = new Vector2(floor.x - feet.x, floor.z - feet.z).magnitude;
        Logger.Write(left > 0.3f ? LogLevel.Warning : LogLevel.Debug, $"[{(left > 0.3f ? "Battle.YouPlacedOff" : "Battle.YouPlaced")}] {feet.Describe()}, {left:0.00} m from {floor.Describe()} (first try {off.magnitude:0.00} m off)");
    }
}
