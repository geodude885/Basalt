using Il2CppRUMBLE.Players;
using UnityEngine;

namespace BossBattles.Actors;

internal sealed class ActorRegistry
{
    private readonly List<Actor> _actors = [];

    public IReadOnlyList<Actor> All => _actors;

    public void Add(Actor actor) => _actors.Add(actor);

    public void Remove(Actor actor) => _actors.Remove(actor);

    public Actor? Find(Player? player) => player == null ? null : _actors.FirstOrDefault(a => a.Owns(player));

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
