using UnityEngine;

namespace BossBattles.Actors;

/// <summary>Who drives an actor.</summary>
internal enum ActorControl
{
    /// <summary>The mod: <see cref="ActorInput"/> for sticks, a held pose for head and hands. What battles use.</summary>
    Bot,

    /// <summary>Your head and hands, and your controllers - the actor copies you. For testing.</summary>
    Mirror,
}

/// <summary>
/// What a bot actor "presses" this frame. Read by the ProcessInput replacement in
/// <c>Patches/ActorPatches</c>. Sticks are in the game's own units: -1..1 per axis.
/// </summary>
internal sealed class ActorInput
{
    public Vector2 Walk { get; set; }

    public Vector2 Turn { get; set; }

    /// <summary>Keep turning the actor to face you, through the game's own PlayerMovement.Rotate.</summary>
    public bool FaceYou { get; set; }

    public void Clear()
    {
        Walk = Vector2.zero;
        Turn = Vector2.zero;
    }
}
