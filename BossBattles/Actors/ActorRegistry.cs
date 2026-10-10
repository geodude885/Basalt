using Il2CppRUMBLE.Players;
using UnityEngine;

namespace BossBattles.Actors;

/// <summary>
/// Every actor in play. Patches ask it "is this player one of ours?" (<see cref="Find(Player?)"/>),
/// so any number of actors get the same treatment.
/// </summary>
internal sealed class ActorRegistry
{
    private readonly List<Actor> _actors = [];

    public IReadOnlyList<Actor> All => _actors;

    public void Add(Actor actor) => _actors.Add(actor);

    public void Remove(Actor actor) => _actors.Remove(actor);

    public Actor? Find(Player? player)
    {
        if (player == null) return null;

        foreach (Actor actor in _actors)
        {
            if (actor.Owns(player)) return actor;
        }

        return null;
    }

    public Actor? Find(PlayerController? controller) => controller == null ? null : Find(controller.assignedPlayer);

    public Actor? FindById(string id) => _actors.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));

    /// <summary>The actor whose body is nearest a point - for keyboard testing.</summary>
    public Actor? First => _actors.FirstOrDefault(a => a.Exists);

    public void Update()
    {
        foreach (Actor actor in _actors.ToArray()) actor.Update();
    }

    public void LateUpdate()
    {
        foreach (Actor actor in _actors.ToArray()) actor.LateUpdate();
    }

    public void DespawnAll()
    {
        foreach (Actor actor in _actors.ToArray()) actor.Despawn();
        _actors.Clear();
    }

    /// <summary>For a scene change: the bodies went with the scene.</summary>
    public void ForgetSceneObjects()
    {
        foreach (Actor actor in _actors) actor.ForgetSceneObjects();
        _actors.Clear();
    }

    /// <summary>Ignore collisions between every actor's body and a structure for a while - see <see cref="Actor.IgnoreStructure"/>.</summary>
    public void IgnoreStructure(GameObject structure, float seconds)
    {
        foreach (Actor actor in _actors) actor.IgnoreStructure(structure, seconds);
    }
}
