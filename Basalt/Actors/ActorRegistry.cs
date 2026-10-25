using Il2CppRUMBLE.Players;
using UnityEngine;

namespace Basalt.Actors;


internal class ActorRegistry
{

    public IReadOnlyList<Actor> Actors => _actors;
    private readonly List<Actor> _actors = [];

    public void Spawn(Actor actor, Vector3 floorPosition, Quaternion facing)
    {
        _actors.Add(actor);
        actor.Spawn(floorPosition, facing);
    }

    public void Despawn(Actor actor)
    {
        actor.Despawn();
        _actors.Remove(actor);
    }

    public void DespawnAll()
    {
        foreach (var actor in _actors) actor.Despawn();
        _actors.Clear();
    }

    public Actor? Find(Player? player) => player == null ? null : _actors.FirstOrDefault(a => a.IsBodyOf(player));

    public Actor? Find(PlayerController? controller) => controller == null ? null : Find(controller.assignedPlayer);

    public void Update()
    {
        foreach (var actor in _actors.ToArray()) actor.Update();
    }

    public void LateUpdate()
    {
        foreach (var actor in _actors.ToArray()) actor.LateUpdate();
    }

    public void ForgetSceneObjects()
    {
        foreach (var actor in _actors) actor.ForgetSceneObjects();
        _actors.Clear();
    }

    // For stopping a structure colliding with the actors until it is clear of them.
    public void IgnoreStructure(GameObject structure, float minimumSeconds, float maximumSeconds)
    {
        foreach (var actor in _actors)
        {
            if (actor.Ready) actor.Body.Collisions.IgnoreStructure(structure, minimumSeconds, maximumSeconds);
        }
    }
}
