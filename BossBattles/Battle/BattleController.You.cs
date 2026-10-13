using BossBattles.Actors;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using UnityEngine;

namespace BossBattles.Battle;

/// <summary>You in a battle: kept in the lane, able to clip damage, on full health at either end.</summary>
internal sealed partial class BattleController
{
    private PlayerHealth? _invincibleBefore;
    private float _nextLaneCheck;

    /// <summary>
    /// The walls' safety net: past one anyway, you are pushed back inside the lane - both bodies
    /// shifted, height kept, and the speed carrying you out stopped.
    /// </summary>
    private void KeepYouInLane()
    {
        if (Time.time < _nextLaneCheck || Chart == null) return;
        _nextLaneCheck = Time.time + 0.1f;

        PlayerController? you = PlayerManager.instance?.localPlayer?.Controller;
        if (you == null) return;

        var rig = ActorRig.From(you);
        Vector3 at = ToLane(rig.Feet);
        var zoned = Mode != BattleMode.Record;
        float side = (Chart.Lane.Width / 2f) + 0.3f, back = -0.75f;
        var far = zoned ? Chart.Zone.Length + (0.3f / _frame.DepthScale) : Chart.Lane.Length + (0.75f / _frame.DepthScale);
        var above = rig.Vr.position.y - StringHelpers.ToUnity(_frame.Anchor.Position).y;
        var tooHigh = zoned && above > Chart.Zone.Height + 0.8f;
        if (!tooHigh && Math.Abs(at.x) <= side + 0.3f && at.z >= back - 0.3f && at.z <= far + 0.3f) return;

        Vector3 inside = World(Math.Clamp(at.x, -side + 0.2f, side - 0.2f), 0f, Math.Clamp(at.z, back + 0.2f, far - 0.2f));
        Vector3 floor = rig.Feet;
        var shift = new Vector3(inside.x - floor.x, 0f, inside.z - floor.z);
        if (tooHigh) shift.y = -(above - Chart.Zone.Height);
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
    private void KeepYouVulnerable()
    {
        if (Mode == BattleMode.Record) return;

        PlayerController? you = PlayerManager.instance?.localPlayer?.Controller;
        if (you == null) return;

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

    private void RestoreInvincibility()
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

    private static void FullHealth()
    {
        Player? you = PlayerManager.instance?.localPlayer;
        if (you?.Controller == null) return;

        var hp = you.Data.HealthPoints;
        if (hp < PlayerHealth.MAX_PLAYER_HEALTH) you.Controller.PlayerHealth.SetHealth((short)PlayerHealth.MAX_PLAYER_HEALTH, hp, false);
        Logger.Debug($"[Battle.FullHealth] {hp} -> {PlayerHealth.MAX_PLAYER_HEALTH}");
    }

    private static Vector3 YourHead()
    {
        PlayerController? you = PlayerManager.instance?.localPlayer?.Controller;
        return you == null ? Vector3.zero : ActorRig.From(you).Head.position;
    }
}
