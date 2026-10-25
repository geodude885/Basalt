using Basalt.Actors;
using Basalt.Services.Charts;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using UnityEngine;

namespace Basalt.Battle;

internal class Projectiles(ActorsRegistry actors)
{
    // How long after launch an actor can stay out of a projectile's way, until it is clear of them
    private const float ActorGraceAfterLaunch = 2f;

    private const float RiseSeconds = 0.35f;

    // How far above the floor a grounded structure rests, so physics finds no overlap to push out of
    private const float FloorClearance = 0.03f;

    // How long past reaching its target a grounded projectile is still driven
    private const float SlideOvershootSeconds = 0.4f;

    private const RigidbodyConstraints SlideConstraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;

    private readonly MoveEffects _effects = new();
    private readonly Dictionary<int, Projectile> _live = [];
    private readonly List<Slide> _sliding = [];

    private enum SlideCheck
    {
        Drive,
        Paused,
        LetGo,
    }

    public void Spawn(int id, ProjectileEvent projectile, Anchor frame)
    {
        var origin = frame.ToWorld(projectile.From).ToUnity();
        var towardPlayer = -frame.Forward.ToUnity();

        // The floor before the pool fetch, which stands the structure at its origin in the ray's way.
        var floor = GameHelpers.HeightUnder(origin) ?? (origin.y - 1f);
        var pooled = PoolManager.instance.GetPooledObject(projectile.Structure, origin, Quaternion.LookRotation(towardPlayer));
        if (pooled == null)
        {
            Logger.Warning($"[Projectile.NoPool] #{id}: no pool called \"{projectile.Structure}\" - check the chart's structure names (Disc, Ball, Pillar, Wall, RockCube, BoulderBall, SmallRock, LargeRock)");
            return;
        }

        var structure = pooled.GetComponent<Structure>();
        var body = pooled.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }

        actors.IgnoreStructure(pooled.gameObject, projectile.Telegraph, projectile.Telegraph + ActorGraceAfterLaunch);

        var facing = Quaternion.LookRotation(towardPlayer);
        var shape = pooled.GetComponentInChildren<Collider>();
        var height = shape != null ? Math.Max(0.2f, shape.bounds.size.y) : 0.5f;

        // What the game spawns on the ground rests on the floor, whatever height the chart gave.
        if (_effects.IsGrounded(projectile.Structure) || projectile.IsGrounded)
            origin.y += floor + FloorClearance - LowestPoint(pooled.transform, origin, facing);

        Vector3 below = new(origin.x, Math.Min(origin.y, floor) - height, origin.z);
        var rise = Math.Min(RiseSeconds, projectile.Telegraph * 0.8f);
        _effects.Spawn(projectile.Structure, new Vector3(origin.x, floor, origin.z), facing);

        _live[id] = new Projectile(structure, body, origin, facing, below, Time.time, rise, projectile.IsGrounded);
        Logger.Debug($"[Projectile.Spawned] #{id} {projectile.Structure} at {origin.Describe()}, launching in {projectile.Telegraph:F2}s, state {structure?.currentPhysicsState}");
    }

    public void Launch(int id, ProjectileEvent projectile, Vector3 target)
    {
        if (!_live.Remove(id, out var live) || live.Body == null) return;

        if (live.Grounded)
        {
            LaunchGrounded(id, projectile, live, target);
            return;
        }

        var from = live.Hold;
        var velocity = Ballistics.LaunchVelocity(
            from.ToNumerics(), target.ToNumerics(), projectile.Speed, -Physics.gravity.y).ToUnity();

        live.Body.transform.position = from;
        live.Body.isKinematic = false;
        live.Body.linearVelocity = velocity;
        _effects.Punch(from, Quaternion.LookRotation(velocity.sqrMagnitude > 0f ? velocity : Vector3.forward));

        // Turned along its path, unless it rests on the floor: its edge would tip into it.
        if (velocity.sqrMagnitude > 0f && !_effects.IsGrounded(projectile.Structure)) live.Body.transform.rotation = Quaternion.LookRotation(velocity);

        Logger.Debug($"[Projectile.Launched] #{id} {projectile.Structure} -> {target.Describe()} at {velocity.magnitude:F1} m/s, state {live.Structure?.currentPhysicsState}");
    }

    // Brings each waiting projectile up out of the floor, then holds it at its origin
    public void Update()
    {
        UpdateSlides();

        foreach (var live in _live.Values)
        {
            if (live.Body == null) continue;

            var k = live.Rise <= 0f ? 1f : Mathf.Clamp01((Time.time - live.RiseStart) / live.Rise);
            var eased = 1f - ((1f - k) * (1f - k) * (1f - k));
            live.Body.transform.SetPositionAndRotation(Vector3.LerpUnclamped(live.RiseFrom, live.Hold, eased), live.HoldRotation);
            if (!live.Body.isKinematic) live.Body.isKinematic = true;

            if (k >= 1f && live.Grounded && !live.Settled)
            {
                live.Settled = true;
                SetState(live.Structure, Structure.PhysicsState.StableGrounded);
            }
        }
    }

    public void Clear()
    {
        foreach (var live in _live.Values)
        {
            live.Body?.isKinematic = false;
        }

        _live.Clear();
        foreach (var slide in _sliding)
        {
            slide.Body?.constraints = slide.Before;
        }

        _sliding.Clear();
    }

    public void Forget()
    {
        _effects.Forget();
        _live.Clear();
        _sliding.Clear();
    }

    private void LaunchGrounded(int id, ProjectileEvent projectile, Projectile live, Vector3 target)
    {
        var body = live.Body!;
        var from = live.Hold;
        var flat = new Vector3(target.x - from.x, 0f, target.z - from.z);
        var direction = flat.sqrMagnitude > 1e-4f ? flat.normalized : live.HoldRotation * Vector3.forward;
        var velocity = direction * projectile.Speed;

        body.transform.position = from;
        SetState(live.Structure, Structure.PhysicsState.FreeGrounded);
        LetGoOfJoint(live.Structure);
        body.isKinematic = false;

        var before = body.constraints;
        body.constraints = SlideConstraints;
        body.linearVelocity = velocity;
        _effects.Punch(from, Quaternion.LookRotation(direction));

        _sliding.Add(new Slide(body, live.Structure, velocity, before)
        {
            Until = Time.time + (flat.magnitude / Math.Max(0.1f, projectile.Speed)) + SlideOvershootSeconds,
            Last = from,
            LastAt = Time.time,
            DrivenSince = Time.time,
        });
        Logger.Debug($"[Projectile.Launched] #{id} {projectile.Structure} grounded along the floor -> {target.Describe()} at {projectile.Speed:F1} m/s, state {live.Structure?.currentPhysicsState}");
    }

    private void UpdateSlides()
    {
        foreach (var slide in _sliding.ToList())
        {
            if (slide.Body == null || !slide.Body.gameObject.activeInHierarchy)
            {
                _sliding.Remove(slide);
                continue;
            }

            var check = CheckSlide(slide);
            if (check == SlideCheck.Paused) continue;

            if (check == SlideCheck.LetGo || HasStopped(slide) || Time.time >= slide.Until)
            {
                slide.Body.constraints = slide.Before;
                _sliding.Remove(slide);
                continue;
            }

            slide.Body.linearVelocity = slide.Velocity;
        }
    }

    private static SlideCheck CheckSlide(Slide slide)
    {
        var state = slide.Structure?.currentPhysicsState;
        if (state == Structure.PhysicsState.Frozen)
        {
            slide.PausedAt ??= Time.time;
            return SlideCheck.Paused;
        }

        if (slide.PausedAt is { } pausedAt) ResumeAfterHitstop(slide, pausedAt);

        // A hold, or the game settling it.
        if (state is not null and not Structure.PhysicsState.FreeGrounded) return SlideCheck.LetGo;

        // Knocked off course - parried, punched, hit - once the physics has had a step to act on the throw.
        var knocked = Time.time - slide.DrivenSince > 0.05f && Vector3.Dot(slide.Body.linearVelocity, slide.Velocity) < 0.5f * slide.Velocity.sqrMagnitude;
        return knocked ? SlideCheck.LetGo : SlideCheck.Drive;
    }

    private static void ResumeAfterHitstop(Slide slide, float pausedAt)
    {
        slide.PausedAt = null;
        LetGoOfJoint(slide.Structure);
        slide.Until += Time.time - pausedAt;
        slide.Last = slide.Body.position;
        slide.LastAt = Time.time;
        slide.DrivenSince = Time.time;
        slide.Body.isKinematic = false;
        slide.Body.constraints = SlideConstraints;
        slide.Body.linearVelocity = slide.Velocity;
    }

    private static bool HasStopped(Slide slide)
    {
        var since = Time.time - slide.LastAt;
        if (since < 0.15f) return false;

        var moved = Vector3.Distance(slide.Body.position, slide.Last);
        slide.Last = slide.Body.position;
        slide.LastAt = Time.time;
        return moved < slide.Velocity.magnitude * since * 0.25f;
    }

    private static float LowestPoint(Transform structure, Vector3 at, Quaternion facing)
    {
        structure.SetPositionAndRotation(at, facing);
        Physics.SyncTransforms();

        var lowest = float.MaxValue;
        foreach (var c in structure.GetComponentsInChildren<Collider>())
        {
            if (c != null && c.enabled && !c.isTrigger) lowest = Math.Min(lowest, c.bounds.min.y);
        }

        return lowest == float.MaxValue ? at.y : lowest;
    }

    private static void SetState(Structure? structure, Structure.PhysicsState state)
    {
        if (structure == null) return;

        try
        {
            structure.CurrentPhysicsState = state;
        }
        catch (Exception e)
        {
            Logger.Error($"[Projectile.StateFailed] {state}: {e.Message}");
        }
    }

    // A pooled structure can come with a joint still holding it; detached, as the game's grounded spawn does.
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

    private record Projectile(Structure? Structure, Rigidbody? Body, Vector3 Hold, Quaternion HoldRotation, Vector3 RiseFrom, float RiseStart, float Rise, bool Grounded)
    {
        public bool Settled { get; set; }
    }

    private record Slide(Rigidbody Body, Structure? Structure, Vector3 Velocity, RigidbodyConstraints Before)
    {
        public float Until { get; set; }
        public Vector3 Last { get; set; }
        public float LastAt { get; set; }
        public float DrivenSince { get; set; }
        public float? PausedAt { get; set; }
    }
}
