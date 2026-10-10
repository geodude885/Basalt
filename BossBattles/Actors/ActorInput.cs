using UnityEngine;

namespace BossBattles.Actors;

/// <summary>Who drives an actor.</summary>
internal enum ActorControl
{
    /// <summary>The mod: <see cref="ActorInput"/> for sticks, a pose, clip or take for head and hands.</summary>
    Bot,

    /// <summary>The actor copies your head, hands and controllers.</summary>
    Mirror,
}

/// <summary>The sticks a bot actor holds this frame, -1..1 per axis; read by ActorPatches' ProcessInput replacement.</summary>
internal sealed class ActorInput
{
    public Vector2 Walk { get; set; }

    public Vector2 Turn { get; set; }

    /// <summary>Keep turning the actor to face you.</summary>
    public bool FaceYou { get; set; }

    public void Clear()
    {
        Walk = Vector2.zero;
        Turn = Vector2.zero;
    }
}
