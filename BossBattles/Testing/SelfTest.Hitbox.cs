using System.Collections;
using BossBattles.Actors;
using BossBattles.Battle;
using BossBattles.Core;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Pools;
using UnityEngine;

namespace BossBattles.Testing;

/// <summary>
/// Does a structure thrown at the boss hit it? Two balls at its chest from 4 m in front, early in the
/// battle (later they knocked the boss's own cube about and reset the cleanup check):
/// a plain one, and one marked as a chart projectile just thrown (actors ignore those for a while,
/// so a shot launched beside the boss does not bump off it) - the case of a projectile punched
/// straight back. Each must stop at the boss, not end up behind it.
/// </summary>
internal static partial class SelfTest
{
    private static string? _hitboxProblem;

    private static IEnumerator CheckActorHitbox()
    {
        var deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        while ((Mod.Battle.State != BattleState.Running || Mod.Battle.ChartTime < 0.8) && Time.realtimeSinceStartup < deadline) yield return null;

        Actor? actor = Mod.Actors.All.FirstOrDefault(a => a.Ready && a.Exists);
        SkinnedMeshRenderer? mesh = actor?.Player?.Controller?.GetComponentInChildren<SkinnedMeshRenderer>();
        if (actor == null || mesh == null || Mod.Battle.LaneOrigin is not { } origin)
        {
            _hitboxProblem = "hitbox check: no actor to throw at";
            yield break;
        }

        var results = new List<string>();
        foreach (var recentProjectile in new[] { false, true })
        {
            Vector3 chest = mesh.bounds.center + (Vector3.up * 0.2f);
            Vector3 towardYou = ActorRig.Flat(origin - chest);
            Vector3 from = chest + (towardYou * 4f);
            Vector3 along = (chest - from).normalized;

            PooledMonoBehaviour? pooled = PoolManager.instance.GetPooledObject("Ball", from, Quaternion.identity);
            Rigidbody? body = pooled?.GetComponent<Rigidbody>();
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

        Mod.Say($"[SelfTest] hitbox: {string.Join(", ", results)}");
    }
}
