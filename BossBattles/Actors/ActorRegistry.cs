using Il2CppRUMBLE.Players;
using UnityEngine;

namespace BossBattles.Actors;

/// <summary>Every actor in play; patches ask it whether a player is one of ours.</summary>
internal sealed class ActorRegistry
{
    private readonly List<Actor> _actors = [];

    public IReadOnlyList<Actor> All => _actors;

    public Actor? First => _actors.FirstOrDefault(a => a.Exists);

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

    public void Update()
    {
        foreach (Actor actor in _actors.ToArray()) actor.Update();
    }

    public void LateUpdate()
    {
        foreach (Actor actor in _actors.ToArray()) actor.LateUpdate();
    }

    /// <summary>For a scene change: the bodies went with the scene.</summary>
    public void ForgetSceneObjects()
    {
        foreach (Actor actor in _actors) actor.ForgetSceneObjects();
        _actors.Clear();
    }

    /// <inheritdoc cref="Actor.IgnoreStructure"/>
    public void IgnoreStructure(GameObject structure, float seconds)
    {
        foreach (Actor actor in _actors) actor.IgnoreStructure(structure, seconds);
    }
}
