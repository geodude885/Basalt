using System.Collections;
using Basalt.Services.Charts;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using MelonLoader;
using UnityEngine;

namespace Basalt.Battle;

internal class BattlePlayer
{
    private PlayerHealth? _invincibleBefore;
    private float _nextLaneCheck;
    private int _placement;

    public static Player? Player => PlayerManager.instance?.localPlayer;
    public static PlayerRig? Rig => Player?.Controller is { } playerController ? PlayerRig.From(playerController) : null;
    public static Vector3 Head => Rig?.Head.position ?? Vector3.zero;

    public void Place(Vector3 floorPosition, Quaternion facing)
    {
        if (Player?.Controller is not { } playerController) return;

        var rig = PlayerRig.From(playerController);
        var eyeHeight = rig.Head.position.y - rig.Vr.position.y;
        playerController.PlayerMovement.Reposition(floorPosition + (Vector3.up * eyeHeight), facing);
        MelonCoroutines.Start(CorrectPlacement(playerController, floorPosition, facing, eyeHeight, ++_placement));
    }

    public void KeepInLane(Anchor frame, Chart chart, bool zoned)
    {
        if (Time.time < _nextLaneCheck || Player?.Controller is not { } playerController) return;
        _nextLaneCheck = Time.time + 0.1f;

        var rig = PlayerRig.From(playerController);
        var at = frame.ToLocal(rig.Feet.ToNumerics()).ToUnity();
        float side = (chart.Lane.Width / 2f) + 0.3f, back = -0.75f;
        var far = zoned ? chart.Zone.Length + 0.3f : chart.Lane.Length + 0.75f;
        var above = rig.Vr.position.y - frame.Position.Y;
        var tooHigh = zoned && above > chart.Zone.Height + 0.8f;
        if (!tooHigh && Math.Abs(at.x) <= side + 0.3f && at.z >= back - 0.3f && at.z <= far + 0.3f) return;

        var inside = frame.ToWorld(new System.Numerics.Vector3(Math.Clamp(at.x, -side + 0.2f, side - 0.2f), 0f, Math.Clamp(at.z, back + 0.2f, far - 0.2f))).ToUnity();
        var floor = rig.Feet;
        var shift = new Vector3(inside.x - floor.x, 0f, inside.z - floor.z);
        if (tooHigh) shift.y = -(above - chart.Zone.Height);
        var physics = playerController.PlayerPhysics;
        foreach (var body in new[] { physics.PhysicsRigidbody, physics.VRrigidbody })
        {
            if (body == null) continue;
            body.position += shift;
            var v = body.linearVelocity;
            var outward = shift.sqrMagnitude > 1e-6f ? -shift.normalized : Vector3.zero;
            var away = Vector3.Dot(v, outward);
            if (away > 0f) body.linearVelocity = v - (outward * away);
        }

        Logger.Debug($"[Battle.YouLeftTheLane] at lane x {at.x:0.0}, z {at.z:0.0}{(tooHigh ? $", {above:0.0} m up" : "")} - put back");
    }

    public void KeepVulnerable()
    {
        if (Player?.Controller is not { } playerController) return;

        try
        {
            var health = playerController.PlayerHealth;
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
        var localPlayer = Player;
        if (localPlayer?.Controller == null) return;

        var hp = localPlayer.Data.HealthPoints;
        if (hp < PlayerHealth.MAX_PLAYER_HEALTH) localPlayer.Controller.PlayerHealth.SetHealth((short)PlayerHealth.MAX_PLAYER_HEALTH, hp, false);
        Logger.Debug($"[Battle.FullHealth] {hp} -> {PlayerHealth.MAX_PLAYER_HEALTH}");
    }

    private IEnumerator CorrectPlacement(PlayerController playerController, Vector3 floor, Quaternion facing, float eyeHeight, int placement)
    {
        yield return null;
        yield return null;
        if (placement != _placement || playerController == null) yield break;

        var feet = PlayerRig.From(playerController).Feet;
        var off = new Vector3(floor.x - feet.x, 0f, floor.z - feet.z);
        if (off.magnitude > 0.15f) playerController.PlayerMovement.Reposition(floor + off + (Vector3.up * eyeHeight), facing);
    }
}
