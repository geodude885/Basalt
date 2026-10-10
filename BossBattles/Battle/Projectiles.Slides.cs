using BossBattles.Core;
using BossBattles.Services.Charts;
using Il2CppRUMBLE.MoveSystem;
using UnityEngine;

namespace BossBattles.Battle;

/// <summary>
/// Grounded projectiles, thrown along the floor in the game's sliding grounded state and driven at
/// the chart's speed until they arrive, are stopped, parried or held. Hitstop pauses the drive, and
/// another move's hit sends it on the way it was hit.
/// </summary>
internal sealed partial class Projectiles
{
    /// <summary>How long past reaching its target a grounded projectile is still driven.</summary>
    private const float SlideOvershootSeconds = 0.4f;

    /// <summary>How long after a move's hit the drive waits for the game's force to land.</summary>
    private const float MoveSettleSeconds = 0.1f;

    /// <summary>Slower than this after a move's hit, it was stopped rather than sent anywhere.</summary>
    private const float StoppedByMoveSpeed = 1f;

    /// <summary><see cref="Touched"/>'s answer for "not driven this frame, but not let go either".</summary>
    private const string Paused = "paused";

    private const RigidbodyConstraints SlideConstraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;

    private readonly List<Slide> _sliding = [];

    public int GroundedLaunched { get; private set; }

    public Rigidbody? LastGroundedBody { get; private set; }

    private void LaunchGrounded(int id, ProjectileEvent projectile, Projectile live, Vector3 target)
    {
        Rigidbody body = live.Body!;
        Vector3 from = live.Hold;
        var flat = new Vector3(target.x - from.x, 0f, target.z - from.z);
        Vector3 direction = flat.sqrMagnitude > 1e-4f ? flat.normalized : live.HoldRotation * Vector3.forward;
        Vector3 velocity = direction * projectile.Speed;

        // The state first: setting it switches the kinematic and gravity flags, wiping a velocity set before.
        body.transform.position = from;
        SetState(live.Structure, Structure.PhysicsState.FreeGrounded);
        LetGoOfJoint(live.Structure);
        body.isKinematic = false;

        // Set, not added to: a settled grounded body is left with FreezeAll.
        RigidbodyConstraints before = body.constraints;
        body.constraints = SlideConstraints;
        body.linearVelocity = velocity;
        MoveEffects.Punch(from, Quaternion.LookRotation(direction));

        var seconds = (flat.magnitude / Math.Max(0.1f, projectile.Speed)) + SlideOvershootSeconds;
        _sliding.Add(new Slide(id, body, velocity, Time.time + seconds, before)
        {
            Last = from,
            LastAt = Time.time,
            DrivenSince = Time.time,
            Seconds = seconds,
            Structure = live.Structure,
            Config = live.Structure?.Processable?.LastStackConfiguration?.Pointer ?? IntPtr.Zero,
        });
        LastGroundedBody = body;
        Launched++;
        GroundedLaunched++;
        _checks.Add(new FlightCheck(id, projectile.Structure, body.transform, from, Time.time, projectile.Speed));
        Mod.Note("Projectile.Launched", $"#{id} {projectile.Structure} grounded along the floor -> {Describe.Vector(target)} at {projectile.Speed:F1} m/s, state {live.Structure?.currentPhysicsState}", echo: false);
    }

    private void UpdateSlides()
    {
        for (var i = _sliding.Count - 1; i >= 0; i--)
        {
            Slide slide = _sliding[i];
            Rigidbody body = slide.Body;
            var gone = body == null || !body.gameObject.activeInHierarchy;
            var touched = gone ? null : Touched(slide, body!);
            if (touched == Paused) continue;
            if (touched != null)
            {
                body!.constraints = slide.Before;
                _sliding.RemoveAt(i);
                _checks.RemoveAll(c => c.Id == slide.Id);
                Mod.Note("Projectile.SlideReleased", $"#{slide.Id}: {touched} - left to the game", echo: false);
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
                _sliding.RemoveAt(i);
                if (stopped) Mod.Note("Projectile.SlideStopped", $"#{slide.Id}: hit something on the floor", echo: false);
                continue;
            }

            body!.linearVelocity = slide.Velocity;
        }
    }

    /// <summary>Why a slide should stop being driven, <see cref="Paused"/> to leave it this frame, or null to keep going.</summary>
    private string? Touched(Slide slide, Rigidbody body)
    {
        Structure? structure = slide.Structure;
        if (structure != null)
        {
            if (structure.currentPhysicsState == Structure.PhysicsState.Frozen)
            {
                if (slide.PausedAt == null)
                {
                    slide.PausedAt = Time.time;
                    _checks.RemoveAll(c => c.Id == slide.Id);
                }

                return Paused;
            }

            if (slide.PausedAt is { } pausedAt)
            {
                slide.PausedAt = null;
                Resume(slide, Time.time - pausedAt);
                Mod.Note("Projectile.SlideHitstop", $"#{slide.Id}: on again after {Time.time - pausedAt:0.00} s of hitstop", echo: false);
            }

            IntPtr config = structure.Processable?.LastStackConfiguration?.Pointer ?? IntPtr.Zero;
            if (config != slide.Config)
            {
                slide.Config = config;
                var move = structure.Processable?.LastStackConfiguration?.ExecutedStack?.CachedName ?? "?";
                if (Stops(move)) return $"a move ({move})";
                slide.HitBy = move;
                slide.SettleUntil = Time.time + MoveSettleSeconds;
            }

            if (structure.currentPhysicsState != Structure.PhysicsState.FreeGrounded) return $"state {structure.currentPhysicsState}";

            if (slide.HitBy != null)
            {
                if (Time.time < slide.SettleUntil) return Paused;

                var sent = new Vector3(body.linearVelocity.x, 0f, body.linearVelocity.z);
                var move = slide.HitBy;
                slide.HitBy = null;
                if (sent.magnitude < StoppedByMoveSpeed) return $"stopped by a move ({move})";

                // At least as fast as it came: a light flick turns it rather than slowing it.
                slide.Velocity = sent.normalized * Math.Max(sent.magnitude, slide.Velocity.magnitude);
                slide.Until = Time.time + slide.Seconds;
                Resume(slide, 0f);
                Mod.Note("Projectile.SlideRedirected", $"#{slide.Id}: {move} sent it on at {slide.Velocity.magnitude:0.0} m/s", echo: false);
            }
        }

        // Knocked off course, once the physics has had a step to act on the throw.
        return Time.time - slide.DrivenSince > 0.05f && Vector3.Dot(body.linearVelocity, slide.Velocity) < 0.5f * slide.Velocity.sqrMagnitude
            ? "turned or slowed"
            : null;
    }

    /// <summary>
    /// Drives a slide again from where it is, its deadline moved on by <paramref name="paused"/>. The
    /// game hands the grounded state back with a joint pinning it, which is let go of.
    /// </summary>
    private static void Resume(Slide slide, float paused)
    {
        LetGoOfJoint(slide.Structure);
        slide.Until += paused;
        slide.Last = slide.Body.position;
        slide.LastAt = Time.time;
        slide.DrivenSince = Time.time;
        slide.Body.isKinematic = false;
        slide.Body.constraints = SlideConstraints;
        slide.Body.linearVelocity = slide.Velocity;
    }

    private static bool Stops(string move) => move.Contains("Parry", StringComparison.OrdinalIgnoreCase) || move.Contains("Hold", StringComparison.OrdinalIgnoreCase);

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
            Mod.Note("Projectile.StateFailed", $"joint: {e.Message}");
        }
    }

    private sealed record Slide(int Id, Rigidbody Body, Vector3 Velocity, float Until, RigidbodyConstraints Before)
    {
        public Vector3 Velocity { get; set; } = Velocity;

        public float Until { get; set; } = Until;

        /// <summary>The drive's length at the throw, given again when a move sends it on.</summary>
        public float Seconds { get; init; }

        public float DrivenSince { get; set; }

        public float? PausedAt { get; set; }

        /// <summary>The move that hit it, until the hit has landed.</summary>
        public string? HitBy { get; set; }

        public float SettleUntil { get; set; }

        public Structure? Structure { get; init; }

        /// <summary>The structure's last move seen; a new one means something touched it.</summary>
        public IntPtr Config { get; set; }

        public Vector3 Last { get; set; }

        public float LastAt { get; set; }
    }
}
