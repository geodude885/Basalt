using BossBattles.Services.Charts;
using Il2CppRUMBLE.MoveSystem;
using UnityEngine;

namespace BossBattles.Battle;

/// <summary>
/// Grounded projectiles, thrown along the floor in the game's sliding grounded state and driven at
/// the chart's speed until they arrive or anything else acts on them. Hitstop pauses the drive.
/// </summary>
internal partial class Projectiles
{
    /// <summary>How long past reaching its target a grounded projectile is still driven.</summary>
    private const float SlideOvershootSeconds = 0.4f;

    /// <summary><see cref="Touched"/>'s answer for "not driven this frame, but not let go either".</summary>
    private const string Paused = "paused";

    private const RigidbodyConstraints SlideConstraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;

    private readonly List<Slide> _sliding = [];

    private void LaunchGrounded(int id, ProjectileEvent projectile, Projectile live, Vector3 target)
    {
        var body = live.Body!;
        var from = live.Hold;
        var flat = new Vector3(target.x - from.x, 0f, target.z - from.z);
        var direction = flat.sqrMagnitude > 1e-4f ? flat.normalized : live.HoldRotation * Vector3.forward;
        var velocity = direction * projectile.Speed;

        // The state first: setting it switches the kinematic and gravity flags, wiping a velocity set before.
        body.transform.position = from;
        SetState(live.Structure, Structure.PhysicsState.FreeGrounded);
        LetGoOfJoint(live.Structure);
        body.isKinematic = false;

        // Set, not added to: a settled grounded body is left with FreezeAll.
        var before = body.constraints;
        body.constraints = SlideConstraints;
        body.linearVelocity = velocity;
        _effects.Punch(from, Quaternion.LookRotation(direction));

        var seconds = (flat.magnitude / Math.Max(0.1f, projectile.Speed)) + SlideOvershootSeconds;
        _sliding.Add(new Slide(id, body, velocity, Time.time + seconds, before)
        {
            Last = from,
            LastAt = Time.time,
            DrivenSince = Time.time,
            Structure = live.Structure,
        });
        _checks.Add(new FlightCheck(id, projectile.Structure, body.transform, from, Time.time, projectile.Speed));
        Logger.Debug($"[Projectile.Launched] #{id} {projectile.Structure} grounded along the floor -> {target.Describe()} at {projectile.Speed:F1} m/s, state {live.Structure?.currentPhysicsState}");
    }

    private void UpdateSlides()
    {
        if (_sliding.Count == 0) return;

        foreach (var slide in _sliding.ToList())
        {
            var body = slide.Body;
            var gone = body == null || !body.gameObject.activeInHierarchy;
            var touched = gone ? null : Touched(slide, body!);
            if (touched == Paused) continue;
            if (touched != null)
            {
                body!.constraints = slide.Before;
                _sliding.Remove(slide);
                _checks.RemoveAll(c => c.Id == slide.Id);
                Logger.Debug($"[Projectile.SlideReleased] #{slide.Id}: {touched} - left to the game");
                continue;
            }

            // Stopped by what it hit: hardly moved over the last 0.15 s.
            var stopped = false;
            if (!gone && Time.time - slide.LastAt >= 0.15f)
            {
                var moved = Vector3.Distance(body!.position, slide.Last);
                stopped = moved < slide.Velocity.magnitude * (Time.time - slide.LastAt) * 0.25f;
                slide.Last = body.position;
                slide.LastAt = Time.time;
            }

            if (gone || stopped || Time.time >= slide.Until)
            {
                if (!gone) body!.constraints = slide.Before;
                _sliding.Remove(slide);
                if (stopped) Logger.Debug($"[Projectile.SlideStopped] #{slide.Id}: hit something on the floor");
                continue;
            }

            body!.linearVelocity = slide.Velocity;
        }
    }

    /// <summary>Why a slide should stop being driven, <see cref="Paused"/> to leave it this frame, or null to keep going.</summary>
    private string? Touched(Slide slide, Rigidbody body)
    {
        var state = slide.Structure?.currentPhysicsState;
        if (state == Structure.PhysicsState.Frozen)
        {
            slide.PausedAt ??= Time.time;
            _checks.RemoveAll(c => c.Id == slide.Id);
            return Paused;
        }

        if (slide.PausedAt is { } pausedAt)
        {
            // Driven again from where it is; the game hands the grounded state back with a joint pinning it.
            slide.PausedAt = null;
            LetGoOfJoint(slide.Structure);
            slide.Until += Time.time - pausedAt;
            slide.Last = slide.Body.position;
            slide.LastAt = Time.time;
            slide.DrivenSince = Time.time;
            slide.Body.isKinematic = false;
            slide.Body.constraints = SlideConstraints;
            slide.Body.linearVelocity = slide.Velocity;
            Logger.Debug($"[Projectile.SlideHitstop] #{slide.Id}: on again after {Time.time - pausedAt:0.00} s of hitstop");
        }

        // A hold, or the game settling it.
        if (state is not null and not Structure.PhysicsState.FreeGrounded) return $"state {state}";

        // Knocked off course - parried, punched, hit - once the physics has had a step to act on the throw.
        return Time.time - slide.DrivenSince > 0.05f && Vector3.Dot(body.linearVelocity, slide.Velocity) < 0.5f * slide.Velocity.sqrMagnitude
            ? "turned or slowed"
            : null;
    }

    /// <summary>A pooled structure can come with a joint still holding it; detached, as the game's grounded spawn does.</summary>
    private static void LetGoOfJoint(Structure? structure)
    {
        if (structure?.ActiveJointControl == null) return;

        try
        {
            structure.ActiveJointControl.DetachBody();
            structure.ActiveJointControl = null;
        }
        catch (Exception e)
        {
            Logger.Error($"[Projectile.StateFailed] joint: {e.Message}");
        }
    }

    private record Slide(int Id, Rigidbody Body, Vector3 Velocity, float Until, RigidbodyConstraints Before)
    {

        public float Until { get; set; } = Until;

        public float DrivenSince { get; set; }

        public float? PausedAt { get; set; }

        public Structure? Structure { get; init; }

        public Vector3 Last { get; set; }

        public float LastAt { get; set; }
    }
}
