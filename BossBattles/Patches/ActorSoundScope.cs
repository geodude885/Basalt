using BossBattles.Core;
using Il2CppRUMBLE.Players;

namespace BossBattles.Patches;

/// <summary>
/// Marks "an actor's own code is running right now", so a sound played inside that window is
/// known to be the actor's - which its position alone cannot say. Entered and left by the prefix
/// and postfix pairs in <see cref="ActorPatches"/>; <see cref="Reset"/> runs every frame in case a
/// call ever ends without its postfix.
/// </summary>
internal static class ActorSoundScope
{
    private static int _depth;

    public static bool Active => _depth > 0;

    /// <returns>Whether the scope was entered, which the matching <see cref="Exit"/> needs.</returns>
    public static bool Enter(PlayerController? controller)
    {
        var entered = false;

        PatchGuard.Run(nameof(ActorSoundScope), () =>
        {
            if (Mod.Actors.Find(controller)?.IsSimulated != true) return;

            _depth++;
            entered = true;
        });

        return entered;
    }

    public static void Exit(bool entered)
    {
        if (entered && _depth > 0) _depth--;
    }

    public static void Reset()
    {
        if (_depth == 0) return;

        Mod.Note("Actor.SoundScopeReset", $"depth {_depth} left open at the end of a frame");
        _depth = 0;
    }
}
