using System.Collections;
using BossBattles.Battle;
using Il2CppRUMBLE.Managers;
using UnityEngine;

namespace BossBattles.DevTools.Testing;

/// <summary>
/// Structures thrown at the boss hit it: a plain ball, and one marked as a just-launched chart
/// projectile (which actors briefly ignore), each must stop at the boss rather than pass through.
/// </summary>
internal static partial class SelfTest
{
    private static string? _hitboxProblem;

    private static IEnumerator CheckActorHitbox()
    {
        var deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        while ((Mod.Battle.State != BattleState.Running || Mod.Battle.ChartTime < 0.8) && Time.realtimeSinceStartup < deadline) yield return null;

        var actor = Mod.Actors.All.FirstOrDefault(a => a.Ready && a.Exists);
        var mesh = actor?.Player?.Controller?.GetComponentInChildren<SkinnedMeshRenderer>();
        if (actor == null || mesh == null || LaneOrigin is not { } origin)
        {
            _hitboxProblem = "hitbox check: no actor to throw at";
            yield break;
        }

        var results = new List<string>();
        foreach (var recentProjectile in new[] { false, true })
        {
            var chest = mesh.bounds.center + (Vector3.up * 0.2f);
            var towardYou = (origin - chest).Flat();
            var from = chest + (towardYou * 4f);
            var along = (chest - from).normalized;

            var pooled = PoolManager.instance.GetPooledObject("Ball", from, Quaternion.identity);
            var body = pooled?.GetComponent<Rigidbody>();
            if (pooled == null || body == null)
            {
                _hitboxProblem = "hitbox check: no ball";
                yield break;
            }

            if (recentProjectile) Mod.Actors.IgnoreStructure(pooled.gameObject, 2.5f);
            body.isKinematic = false;
            body.position = from;
            body.linearVelocity = along * 10f;

            yield return new WaitForSeconds(0.8f);

            var past = Vector3.Dot(body.position - chest, along);
            var through = past > 0.6f;
            var what = recentProjectile ? "a just-thrown chart projectile" : "a plain ball";
            results.Add($"{what}: {(through ? $"went THROUGH ({past:0.0} m behind)" : $"stopped ({past:0.0} m)")}");
            if (through) _hitboxProblem = (_hitboxProblem == null ? "" : _hitboxProblem + "; ") + $"{what} thrown at the boss went through it";

            body.linearVelocity = Vector3.zero;
            pooled.gameObject.SetActive(false);
            yield return new WaitForSeconds(0.2f);
        }

        Logger.Info($"[SelfTest] hitbox: {string.Join(", ", results)}");

        // Lethal hits through the game's own ReduceHealth: the actor must live on, on some health.
        foreach (var amount in new short[] { 19, 19, 30 })
        {
            actor.Player!.Controller.PlayerHealth.ReduceHealth(amount, true);
            yield return new WaitForSeconds(0.3f);
            var hp = actor.Exists ? actor.Player!.Data.HealthPoints : (short)0;
            Logger.Info($"[SelfTest] lethal hit of {amount}: the actor {(actor.Exists ? $"lives on {hp} HP" : "is gone")}");
            if (!actor.Exists || hp <= 0) _hitboxProblem = (_hitboxProblem == null ? "" : _hitboxProblem + "; ") + $"a hit of {amount} killed the actor";
        }
    }
}
