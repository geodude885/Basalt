using Il2CppRUMBLE.Players;

namespace BossBattles.Patches;

/// <summary>
/// Marks that an actor's own code is running, so a sound played meanwhile is known to be the actor's
/// (its position cannot say). <see cref="Reset"/> runs each frame in case a postfix never ran.
/// </summary>
internal static class ActorSoundScope
{
    private static int _depth;

    public static bool Active => _depth > 0;

    /// <returns>Whether the scope was entered, for the matching <see cref="Exit"/>.</returns>
    public static bool Enter(PlayerController? controller)
    {
        var entered = false;

        PatchGuard.Run(nameof(ActorSoundScope), () =>
        {
            if (Mod.Actors.Find(controller)?.Exists != true) return;

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

        Logger.Warning($"[Actor.SoundScopeReset] depth {_depth} left open at the end of a frame");
        _depth = 0;
    }
}
