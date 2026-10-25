using System.Collections;
using Il2CppRUMBLE.Players;
using MelonLoader;
using UnityEngine;

namespace Basalt.Actors;

internal class ActorCollisions(PlayerController controller)
{
    // How far a structure must be from any part of the actor before it can hit the actor.
    private const float ClearOfActor = 0.3f;

    private Collider[]? _colliders;

    public IEnumerable<Collider> All => controller == null ? Array.Empty<Collider>() : controller.GetComponentsInChildren<Collider>(true);

    // Stops a structure colliding with the actor for at least `minimumSeconds`, then until it is clear of the actor
    // or `maximumSeconds` have passed.
    public void IgnoreStructure(GameObject structure, float minimumSeconds, float maximumSeconds)
    {
        _colliders ??= controller.GetComponentsInChildren<Collider>(true);
        Collider[] structureColliders = structure.GetComponentsInChildren<Collider>(true);

        foreach (var actorCollider in _colliders)
        {
            foreach (var structureCollider in structureColliders) Physics.IgnoreCollision(actorCollider, structureCollider, true);
        }

        MelonCoroutines.Start(RestoreWhenClear(_colliders, structureColliders, minimumSeconds, maximumSeconds));
    }

    private IEnumerator RestoreWhenClear(Collider[] actorColliders, Collider[] structureColliders, float minimumSeconds, float maximumSeconds)
    {
        var heldUntil = Time.time + minimumSeconds;
        var until = Time.time + maximumSeconds;
        while (Time.time < until && controller != null && structureColliders.Any(c => c != null))
        {
            if (Time.time >= heldUntil && IsClear(actorColliders, structureColliders)) break;
            yield return null;
        }

        foreach (var actorCollider in actorColliders)
        {
            if (actorCollider == null) continue;
            foreach (var structureCollider in structureColliders)
            {
                if (structureCollider != null) Physics.IgnoreCollision(actorCollider, structureCollider, false);
            }
        }
    }

    // Clear of the actor's whole body, hands included: a mimed throw reaches out towards the shot.
    private static bool IsClear(Collider[] actorColliders, Collider[] structureColliders)
    {
        var actorBounds = actorColliders.Where(c => c != null && c.enabled && !c.isTrigger).Select(c => c.bounds).Aggregate((all, next) =>
        {
            all.Encapsulate(next);
            return all;
        });
        actorBounds.Expand(ClearOfActor * 2f);
        return structureColliders.Where(c => c != null).All(c => !c.bounds.Intersects(actorBounds));
    }
}
