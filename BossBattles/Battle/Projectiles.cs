using BossBattles.Core;
using BossBattles.Services.Charts;
using Il2CppRUMBLE.Environment;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Pools;
using UnityEngine;

namespace BossBattles.Battle;

/// <summary>
/// A battle's projectiles: real structures from the game's pools that come up out of the floor like
/// a player's spawn - with the spawn move's own sound and effect (<see cref="MoveEffects"/>) - and
/// are held at their origin until the throw, then launched on a ballistic path that arrives at the
/// target in distance / speed seconds (<see cref="Ballistics"/>), with the punch's sound and effect.
/// Outside the move system, so a chart can throw faster than a player could spawn. Actors ignore
/// them for a while, so a shot launched beside an actor is not deflected by its body.
/// </summary>
internal sealed class Projectiles
{
    /// <summary>How long after launch actors stay out of a projectile's way.</summary>
    private const float ActorGraceAfterLaunch = 2f;

    /// <summary>How long after launch each projectile is checked to be flying.</summary>
    private const float FlightCheckSeconds = 0.3f;

    /// <summary>How long a structure takes to come up out of the floor - less when thrown sooner.</summary>
    private const float RiseSeconds = 0.35f;

    private readonly Dictionary<int, Projectile> _live = [];
    private readonly List<FlightCheck> _checks = [];

    public int Spawned { get; private set; }

    public int Launched { get; private set; }

    public int Failed { get; private set; }

    public void Spawn(int id, ProjectileEvent projectile, LaneFrame frame)
    {
        Vector3 origin = Describe.ToUnity(frame.ToWorld(projectile.From));
        Vector3 towardYou = -Describe.ToUnity(frame.Forward);

        // The floor first: fetched from its pool, the structure already stands at its origin, and
        // the ray found its top (a disc 1.5 m up) instead of the ground.
        var floor = Arena.TryFloorHeight(origin, out var ground) ? ground : origin.y - 1f;
        PooledMonoBehaviour pooled = PoolManager.instance.GetPooledObject(projectile.Structure, origin, Quaternion.LookRotation(towardYou));
        if (pooled == null)
        {
            Failed++;
            Mod.Note("Projectile.NoPool", $"#{id}: no pool called \"{projectile.Structure}\" - check the chart's structure names (Disc, Ball, Pillar, Wall, RockCube, BoulderBall, SmallRock, LargeRock)");
            return;
        }

        Structure structure = pooled.GetComponent<Structure>();
        Rigidbody body = pooled.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }

        Mod.Actors.IgnoreStructure(pooled.gameObject, projectile.Telegraph + ActorGraceAfterLaunch);

        // Up out of the floor under its origin, as a player's spawn comes up: from just below the
        // ground to the origin, quickly, with the spawn move's sound and effect where it breaks out.
        var facing = Quaternion.LookRotation(towardYou);
        Collider? shape = pooled.GetComponentInChildren<Collider>();
        var height = shape != null ? Math.Max(0.2f, shape.bounds.size.y) : 0.5f;

        // What the game spawns on the ground - a disc, a cube, a pillar, a wall - rests on the floor
        // there, whatever height the chart gave: it comes up out of the ground and stays on it.
        if (MoveEffects.IsGrounded(projectile.Structure) || projectile.IsGrounded)
        {
            // By its colliders' real lowest point, not half its height: the pivot is not always the centre.
            origin.y += floor + FloorClearance - LowestPoint(pooled.transform, origin, facing);

            // A grounded structure well above the lane's own floor found something else to stand on;
            // well below it, it is over a ledge - in the Gym a disc there broke on the ledge's edge.
            var laneFloor = Describe.ToUnity(frame.Origin).y;
            if (floor - laneFloor > 1f) Mod.Note("Projectile.OffTheFloor", $"#{id} {projectile.Structure}: floor found {floor - laneFloor:0.00} m above the lane's");
            else if (laneFloor - floor > 1f) Mod.Note("Projectile.BelowTheLane", $"#{id} {projectile.Structure}: the ground there is {laneFloor - floor:0.00} m below your end of the lane - it may hit the slope", echo: false);
        }
        Vector3 below = new(origin.x, Math.Min(origin.y, floor) - height, origin.z);
        var rise = Math.Min(RiseSeconds, projectile.Telegraph * 0.8f);
        MoveEffects.Spawn(projectile.Structure, new Vector3(origin.x, floor, origin.z), facing);

        // The game removes structures outside the scene's structure boundary - the moment they move.
        SceneBoundary bounds = SceneBoundary.CurrentActiveStructuresSceneBoundary;
        if (bounds != null && !bounds.Contains(origin))
            Mod.Note("Projectile.OutOfBounds", $"#{id} {projectile.Structure} at {Describe.Vector(origin)} is outside the scene's structure boundary - the game will remove it");

        _live[id] = new Projectile(structure, body, origin, facing) { From = below, RiseStart = Time.time, Rise = rise, Grounded = projectile.IsGrounded };
        Spawned++;
        Mod.Note("Projectile.Spawned", $"#{id} {projectile.Structure} at {Describe.Vector(origin)}, launching in {projectile.Telegraph:F2}s, state {structure?.currentPhysicsState}", echo: false);
    }

    /// <summary>How far above the floor a grounded structure rests, so physics finds no overlap to push out of.</summary>
    private const float FloorClearance = 0.03f;

    /// <summary>The lowest point of a structure's solid colliders, posed at <paramref name="at"/>.</summary>
    private static float LowestPoint(Transform structure, Vector3 at, Quaternion facing)
    {
        structure.SetPositionAndRotation(at, facing);
        Physics.SyncTransforms();

        var lowest = float.MaxValue;
        foreach (Collider c in structure.GetComponentsInChildren<Collider>())
        {
            if (c != null && c.enabled && !c.isTrigger) lowest = Math.Min(lowest, c.bounds.min.y);
        }

        return lowest == float.MaxValue ? at.y : lowest;
    }

    public void Launch(int id, ProjectileEvent projectile, Vector3 target)
    {
        if (!_live.Remove(id, out Projectile? live) || live.Body == null) return;

        Vector3 from = live.Hold;
        if (live.Grounded)
        {
            LaunchGrounded(id, projectile, live, target);
            return;
        }

        Vector3 velocity = Describe.ToUnity(Ballistics.LaunchVelocity(
            Describe.ToNumerics(from), Describe.ToNumerics(target), projectile.Speed, -Physics.gravity.y));

        live.Body.transform.position = from;
        live.Body.isKinematic = false;
        live.Body.linearVelocity = velocity;
        MoveEffects.Punch(from, Quaternion.LookRotation(velocity.sqrMagnitude > 0f ? velocity : Vector3.forward));
        // Turned along its path - but not a grounded one, whose edge would tip into the floor.
        if (velocity.sqrMagnitude > 0f && !MoveEffects.IsGrounded(projectile.Structure)) live.Body.transform.rotation = Quaternion.LookRotation(velocity);

        Launched++;
        _checks.Add(new FlightCheck(id, projectile.Structure, live.Body.transform, from, Time.time, projectile.Speed));
        Mod.Note("Projectile.Launched", $"#{id} {projectile.Structure} -> {Describe.Vector(target)} at {velocity.magnitude:F1} m/s, state {live.Structure?.currentPhysicsState}", echo: false);
    }

    /// <summary>Grounded projectiles thrown so far - the self-test reads it.</summary>
    public int GroundedLaunched { get; private set; }

    /// <summary>
    /// A grounded projectile along the floor, as a straight sends a grounded structure: the game's
    /// sliding grounded state (FreeGrounded - the grounded damage tier), height and tilt held, and
    /// driven at the chart's speed towards the target's spot on the floor until it is there or hits
    /// something. The game's own push does it with a joint; this keeps the chart's aim and speed.
    /// </summary>
    private void LaunchGrounded(int id, ProjectileEvent projectile, Projectile live, Vector3 target)
    {
        Rigidbody body = live.Body!;
        Vector3 from = live.Hold;
        var flat = new Vector3(target.x - from.x, 0f, target.z - from.z);
        Vector3 direction = flat.sqrMagnitude > 1e-4f ? flat.normalized : live.HoldRotation * Vector3.forward;
        Vector3 velocity = direction * projectile.Speed;

        // The state first: setting it switches the body's kinematic and gravity flags, which wiped a
        // velocity set before it (the first cube stopped dead in its first frame).
        body.transform.position = from;
        SetState(live.Structure, Structure.PhysicsState.FreeGrounded);
        LetGoOfJoint(live.Structure);
        body.isKinematic = false;
        // Set, not added to: the grounded state leaves the body frozen in place (constraints FreezeAll
        // was found on a settled cube), and adding to that kept it there.
        RigidbodyConstraints before = body.constraints;
        body.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;
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

    /// <summary>How long past reaching its target a grounded projectile is still driven.</summary>
    private const float SlideOvershootSeconds = 0.4f;

    private readonly List<Slide> _sliding = [];

    /// <summary>The last grounded projectile thrown - the self-test parries and hitstops it.</summary>
    public Rigidbody? LastGroundedBody { get; private set; }

    /// <summary>
    /// Keeps grounded projectiles going - until there, stopped by what they hit, parried or held -
    /// then lets go and leaves them to the game. Driving on regardless overrode a parry or a hold
    /// every frame. The game's hitstop pauses the drive rather than ending it (letting go there left
    /// the cube to friction, stopped dead), and another move's hit sends it on the way it was hit.
    /// </summary>
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
                _checks.RemoveAll(c => c.Id == slide.Id); // the game's now, not the chart's to judge
                Mod.Note("Projectile.SlideReleased", $"#{slide.Id}: {touched} - left to the game", echo: false);
                continue;
            }

            // Stopped by what it hit: hardly moved over the last 0.15 s (not its speed this frame -
            // the physics has not run between the throw and the first look).
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

    /// <summary>
    /// A pooled structure can come with a joint still holding it - grounded, it stayed pinned where it
    /// rose while being driven. Detached, as the game's grounded spawn does.
    /// </summary>
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

    private static void SetState(Structure? structure, Structure.PhysicsState state)
    {
        if (structure == null) return;

        try
        {
            structure.CurrentPhysicsState = state;
        }
        catch (Exception e)
        {
            Mod.Note("Projectile.StateFailed", $"{state}: {e.Message}");
        }
    }

    /// <summary><see cref="Touched"/>'s answer for "not driven this frame, but not let go either".</summary>
    private const string Paused = "paused";

    /// <summary>How long after a move's hit the drive waits for the game's force to land.</summary>
    private const float MoveSettleSeconds = 0.1f;

    /// <summary>Slower than this after a move's hit, it was stopped, not sent anywhere.</summary>
    private const float StoppedByMoveSpeed = 1f;

    /// <summary>
    /// Why a slide should stop being driven, <see cref="Paused"/> to leave it alone this frame, or
    /// null to keep going.
    /// </summary>
    private string? Touched(Slide slide, Rigidbody body)
    {
        Structure? structure = slide.Structure;
        if (structure != null)
        {
            // The game's hitstop: frozen for a moment, then back to the state before. Waited out,
            // and the time it took added on.
            if (structure.currentPhysicsState == Structure.PhysicsState.Frozen)
            {
                if (slide.PausedAt == null)
                {
                    slide.PausedAt = Time.time;
                    _checks.RemoveAll(c => c.Id == slide.Id); // a hitstop is not a stall
                }

                return Paused;
            }

            if (slide.PausedAt is { } pausedAt)
            {
                slide.PausedAt = null;
                Resume(slide, Time.time - pausedAt);
                Mod.Note("Projectile.SlideHitstop", $"#{slide.Id}: on again after {Time.time - pausedAt:0.00} s of hitstop", echo: false);
            }

            // A move worked on it. A parry or a hold stops it; anything else hits it on, and once
            // the hit has landed it goes on the way it was sent.
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

                // At least as fast as it came - a light flick turns it rather than slowing it.
                slide.Velocity = sent.normalized * Math.Max(sent.magnitude, slide.Velocity.magnitude);
                slide.Until = Time.time + slide.Seconds;
                Resume(slide, 0f);
                Mod.Note("Projectile.SlideRedirected", $"#{slide.Id}: {move} sent it on at {slide.Velocity.magnitude:0.0} m/s", echo: false);
            }
        }

        // Knocked off course - the physics has had a step to act on the throw by then.
        return Time.time - slide.DrivenSince > 0.05f && Vector3.Dot(body.linearVelocity, slide.Velocity) < 0.5f * slide.Velocity.sqrMagnitude
            ? "turned or slowed"
            : null;
    }

    /// <summary>
    /// Drives a slide again from where it is now, its deadline moved on by <paramref name="paused"/>.
    /// The game hands the grounded state back with a joint holding it, as at the throw - let go of.
    /// </summary>
    private static void Resume(Slide slide, float paused)
    {
        LetGoOfJoint(slide.Structure);
        slide.Until += paused;
        slide.Last = slide.Body.position;
        slide.LastAt = Time.time;
        slide.DrivenSince = Time.time;
        slide.Body.isKinematic = false;
        slide.Body.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;
        slide.Body.linearVelocity = slide.Velocity;
    }

    /// <summary>Moves that stop a grounded projectile rather than hit it on.</summary>
    private static bool Stops(string move) => move.Contains("Parry", StringComparison.OrdinalIgnoreCase) || move.Contains("Hold", StringComparison.OrdinalIgnoreCase);

    private sealed record Slide(int Id, Rigidbody Body, Vector3 Velocity, float Until, RigidbodyConstraints Before)
    {
        public Vector3 Velocity { get; set; } = Velocity;

        public float Until { get; set; } = Until;

        /// <summary>The drive's length at the throw, given again when a move sends it on.</summary>
        public float Seconds { get; init; }

        /// <summary>When the drive last (re)started - the turned check gives the physics a step after it.</summary>
        public float DrivenSince { get; set; }

        /// <summary>When the game's hitstop froze it, while it is frozen.</summary>
        public float? PausedAt { get; set; }

        /// <summary>The move that hit it, until the hit has landed.</summary>
        public string? HitBy { get; set; }

        public float SettleUntil { get; set; }

        public Structure? Structure { get; init; }

        /// <summary>The structure's last move seen - a new one means something touched it.</summary>
        public IntPtr Config { get; set; }

        public Vector3 Last { get; set; }

        public float LastAt { get; set; }
    }

    /// <summary>
    /// "Launched" only means the velocity was set. A moment later each projectile is checked to
    /// have covered at least half the ground its speed promises - the game could have reset a pooled
    /// structure, held it, or let it drop - and a stall is counted.
    /// </summary>
    private void CheckFlights()
    {
        for (var i = _checks.Count - 1; i >= 0; i--)
        {
            FlightCheck check = _checks[i];
            var age = Time.time - check.LaunchedAt;
            if (age < FlightCheckSeconds) continue;

            _checks.RemoveAt(i);
            if (check.Transform == null) continue;

            var travelled = Vector3.Distance(check.Transform.position, check.From);
            var flying = travelled >= check.Speed * age * 0.5f;
            Mod.Note(flying ? "Projectile.Flying" : "Projectile.Stalled",
                $"#{check.Id} {check.Structure}: {travelled:F2} m in {age:F2}s (speed {check.Speed:F1})", echo: !flying);
        }
    }

    /// <summary>
    /// Brings each waiting projectile up out of the floor, then holds it at its origin - pooled
    /// structures come out with physics of their own.
    /// </summary>
    public void Update()
    {
        CheckFlights();
        UpdateSlides();

        foreach (Projectile live in _live.Values)
        {
            if (live.Body == null) continue;

            var k = live.Rise <= 0f ? 1f : Mathf.Clamp01((Time.time - live.RiseStart) / live.Rise);
            var eased = 1f - ((1f - k) * (1f - k) * (1f - k));
            live.Body.transform.SetPositionAndRotation(Vector3.LerpUnclamped(live.From ?? live.Hold, live.Hold, eased), live.HoldRotation);
            if (!live.Body.isKinematic) live.Body.isKinematic = true;

            // Up and resting on the floor: grounded, as the game's grounded spawn leaves it.
            if (k >= 1f && live.Grounded && !live.Settled)
            {
                live.Settled = true;
                SetState(live.Structure, Structure.PhysicsState.StableGrounded);
            }
        }
    }

    /// <summary>Lets go of anything still telegraphing - it falls as a normal structure.</summary>
    public void Clear()
    {
        foreach (Projectile live in _live.Values)
        {
            live.Body?.isKinematic = false;
        }

        _live.Clear();
        foreach (Slide slide in _sliding)
        {
            slide.Body?.constraints = slide.Before;
        }

        _sliding.Clear();
    }

    /// <summary>For a scene change: the structures went with the scene.</summary>
    public void Forget()
    {
        _live.Clear();
        _checks.Clear();
        _sliding.Clear();
    }

    public void ResetCounts()
    {
        Spawned = 0;
        Launched = 0;
        Failed = 0;
        GroundedLaunched = 0;
    }

    private sealed record Projectile(Structure? Structure, Rigidbody? Body, Vector3 Hold, Quaternion HoldRotation)
    {
        /// <summary>Where it rises from - below the floor; null to appear at its origin.</summary>
        public Vector3? From { get; init; }

        public float RiseStart { get; init; }

        public float Rise { get; init; }

        /// <summary>Sits in the grounded state and is thrown along the floor.</summary>
        public bool Grounded { get; init; }

        /// <summary>Up and put in the grounded state.</summary>
        public bool Settled { get; set; }
    }

    private sealed record FlightCheck(int Id, string Structure, Transform Transform, Vector3 From, float LaunchedAt, float Speed);
}
