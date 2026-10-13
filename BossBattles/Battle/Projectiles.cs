using BossBattles.Arenas;
using BossBattles.Services.Charts;
using Il2CppRUMBLE.Environment;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Pools;
using UnityEngine;

namespace BossBattles.Battle;

/// <summary>
/// A battle's projectiles: structures from the game's pools that rise out of the floor like a
/// player's spawn, are held at their origin through the telegraph, then launched on a ballistic path
/// that arrives in distance / speed seconds. Outside the move system, so a chart can throw faster
/// than a player could spawn.
/// </summary>
internal sealed partial class Projectiles
{
    /// <summary>How long after launch actors stay out of a projectile's way.</summary>
    private const float ActorGraceAfterLaunch = 2f;

    private const float FlightCheckSeconds = 0.3f;
    private const float RiseSeconds = 0.35f;

    /// <summary>How far above the floor a grounded structure rests, so physics finds no overlap to push out of.</summary>
    private const float FloorClearance = 0.03f;

    private readonly Dictionary<int, Projectile> _live = [];
    private readonly List<FlightCheck> _checks = [];

    public void Spawn(int id, ProjectileEvent projectile, LaneFrame frame)
    {
        Vector3 origin = StringHelpers.ToUnity(frame.ToWorld(projectile.From));
        Vector3 towardYou = -StringHelpers.ToUnity(frame.Anchor.Forward);

        // The floor before the pool fetch, which stands the structure at its origin in the ray's way.
        var floor = Arena.TryFloorHeight(origin, out var ground) ? ground : origin.y - 1f;
        PooledMonoBehaviour pooled = PoolManager.instance.GetPooledObject(projectile.Structure, origin, Quaternion.LookRotation(towardYou));
        if (pooled == null)
        {
            Logger.Warning($"[Projectile.NoPool] #{id}: no pool called \"{projectile.Structure}\" - check the chart's structure names (Disc, Ball, Pillar, Wall, RockCube, BoulderBall, SmallRock, LargeRock)");
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

        var facing = Quaternion.LookRotation(towardYou);
        Collider? shape = pooled.GetComponentInChildren<Collider>();
        var height = shape != null ? Math.Max(0.2f, shape.bounds.size.y) : 0.5f;

        // What the game spawns on the ground rests on the floor, whatever height the chart gave.
        if (MoveEffects.IsGrounded(projectile.Structure) || projectile.IsGrounded)
        {
            origin.y += floor + FloorClearance - LowestPoint(pooled.transform, origin, facing);

            var laneFloor = StringHelpers.ToUnity(frame.Anchor.Position).y;
            if (floor - laneFloor > 1f) Logger.Warning($"[Projectile.OffTheFloor] #{id} {projectile.Structure}: floor found {floor - laneFloor:0.00} m above the lane's");
            else if (laneFloor - floor > 1f) Logger.Debug($"[Projectile.BelowTheLane] #{id} {projectile.Structure}: the ground there is {laneFloor - floor:0.00} m below your end of the lane - it may hit the slope");
        }

        Vector3 below = new(origin.x, Math.Min(origin.y, floor) - height, origin.z);
        var rise = Math.Min(RiseSeconds, projectile.Telegraph * 0.8f);
        MoveEffects.Spawn(projectile.Structure, new Vector3(origin.x, floor, origin.z), facing);

        // The game removes structures outside the scene's structure boundary the moment they move.
        SceneBoundary bounds = SceneBoundary.CurrentActiveStructuresSceneBoundary;
        if (bounds != null && !bounds.Contains(origin))
            Logger.Info($"[Projectile.OutOfBounds] #{id} {projectile.Structure} at {StringHelpers.Vector(origin)} is outside the scene's structure boundary - the game will remove it");

        _live[id] = new Projectile(structure, body, origin, facing) { From = below, RiseStart = Time.time, Rise = rise, Grounded = projectile.IsGrounded };
        Logger.Debug($"[Projectile.Spawned] #{id} {projectile.Structure} at {StringHelpers.Vector(origin)}, launching in {projectile.Telegraph:F2}s, state {structure?.currentPhysicsState}");
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

        Vector3 velocity = StringHelpers.ToUnity(Ballistics.LaunchVelocity(
            StringHelpers.ToNumerics(from), StringHelpers.ToNumerics(target), projectile.Speed, -Physics.gravity.y));

        live.Body.transform.position = from;
        live.Body.isKinematic = false;
        live.Body.linearVelocity = velocity;
        MoveEffects.Punch(from, Quaternion.LookRotation(velocity.sqrMagnitude > 0f ? velocity : Vector3.forward));

        // Turned along its path, unless it rests on the floor: its edge would tip into it.
        if (velocity.sqrMagnitude > 0f && !MoveEffects.IsGrounded(projectile.Structure)) live.Body.transform.rotation = Quaternion.LookRotation(velocity);

        _checks.Add(new FlightCheck(id, projectile.Structure, live.Body.transform, from, Time.time, projectile.Speed));
        Logger.Debug($"[Projectile.Launched] #{id} {projectile.Structure} -> {StringHelpers.Vector(target)} at {velocity.magnitude:F1} m/s, state {live.Structure?.currentPhysicsState}");
    }

    /// <summary>Brings each waiting projectile up out of the floor, then holds it at its origin.</summary>
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

            if (k >= 1f && live.Grounded && !live.Settled)
            {
                live.Settled = true;
                SetState(live.Structure, Structure.PhysicsState.StableGrounded);
            }
        }
    }

    /// <summary>Lets go of anything still telegraphing or sliding.</summary>
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

    /// <summary>The lowest point of a structure's solid colliders posed at <paramref name="at"/>; the pivot is not always the centre.</summary>
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

    /// <summary>
    /// Counts a stall when a projectile has not covered half the ground its speed promises: the game
    /// could have reset a pooled structure, held it, or let it drop.
    /// </summary>
    private void CheckFlights()
    {
        if (_checks.Count == 0) return;

        foreach (FlightCheck check in _checks.Where(c => Time.time - c.LaunchedAt >= FlightCheckSeconds).ToList())
        {
            _checks.Remove(check);
            if (check.Transform == null) continue;

            var age = Time.time - check.LaunchedAt;
            var travelled = Vector3.Distance(check.Transform.position, check.From);
            var flying = travelled >= check.Speed * age * 0.5f;
            Logger.Write(flying ? LogLevel.Debug : LogLevel.Warning, $"[{(flying ? "Projectile.Flying" : "Projectile.Stalled")}] #{check.Id} {check.Structure}: {travelled:F2} m in {age:F2}s (speed {check.Speed:F1})");
        }
    }

    private sealed record Projectile(Structure? Structure, Rigidbody? Body, Vector3 Hold, Quaternion HoldRotation)
    {
        /// <summary>Where it rises from, below the floor; null to appear at its origin.</summary>
        public Vector3? From { get; init; }

        public float RiseStart { get; init; }

        public float Rise { get; init; }

        /// <summary>Sits in the grounded state and is thrown along the floor.</summary>
        public bool Grounded { get; init; }

        public bool Settled { get; set; }
    }

    private sealed record FlightCheck(int Id, string Structure, Transform Transform, Vector3 From, float LaunchedAt, float Speed);
}
