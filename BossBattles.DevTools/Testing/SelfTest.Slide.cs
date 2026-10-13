using System.Collections;
using Il2CppRUMBLE.MoveSystem;
using UnityEngine;

namespace BossBattles.DevTools.Testing;

/// <summary>
/// Grounded projectiles stay the game's to play with: one must keep sliding after the game's
/// hitstop (<see cref="Structure.FreezePhysics"/>), another must stay knocked back rather than be dragged on.
/// </summary>
internal static partial class SelfTest
{
    private static string? _slideProblem;

    private static IEnumerator CheckSlides()
    {
        var deadline = Time.realtimeSinceStartup + TimeoutSeconds;

        // 1. Hitstop mid-slide.
        while (GroundedLaunches < 1 && Time.realtimeSinceStartup < deadline) yield return null;
        Rigidbody? first = SlidingBody();
        yield return new WaitForSeconds(0.1f);
        Structure? structure = first?.GetComponent<Structure>();
        if (first == null || structure == null)
        {
            _slideProblem = "slide check: no grounded projectile";
            yield break;
        }

        Vector3 before = first.linearVelocity;
        structure.FreezePhysics(0.15f);
        yield return new WaitForSeconds(0.3f);
        var speed = Vector3.Dot(first.linearVelocity, before.normalized);
        var frozen = structure.currentPhysicsState == Structure.PhysicsState.Frozen || first.isKinematic;
        var hitstop = $"after hitstop: {speed:0.0} of {before.magnitude:0.0} m/s on, state {structure.currentPhysicsState}, kinematic {first.isKinematic}";
        if (frozen || speed < 0.9f * before.magnitude)
            _slideProblem = $"a grounded projectile did not carry on after the game's hitstop ({hitstop})";

        // 2. Knocked back mid-slide.
        while (GroundedLaunches < 2 && Time.realtimeSinceStartup < deadline) yield return null;
        Rigidbody? second = SlidingBody();
        yield return new WaitForSeconds(0.1f);
        if (second == null)
        {
            _slideProblem ??= "slide check: no second grounded projectile";
            yield break;
        }

        Vector3 thrown = second.linearVelocity;
        second.linearVelocity = -thrown;
        yield return new WaitForSeconds(0.25f);
        var back = Vector3.Dot(second.linearVelocity, -thrown.normalized);
        var parry = $"knocked back: {back:0.0} m/s back towards the boss";
        if (back <= 0.5f) _slideProblem ??= $"a grounded projectile knocked back mid-slide was dragged on at you ({parry})";

        Logger.Info($"[SelfTest] slides: {hitstop}; {parry}");
    }
}
