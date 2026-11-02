using Basalt.Actors;
using Il2CppRUMBLE.Players;

namespace Basalt.Patches;

internal static class ActorSoundScope
{
    public static bool Active { get; private set; }

    public static bool Enter(ActorsRegistry actors, PlayerController? controller)
    {
        var entered = false;

        PatchGuard.Run(nameof(ActorSoundScope), () => entered = !Active && actors.Find(controller) != null);

        if (entered) Active = true;
        return entered;
    }

    public static void Exit(bool entered)
    {
        if (entered) Active = false;
    }

    public static void Reset()
    {
        if (!Active) return;

        Logger.Warning("[Actor.SoundScopeReset] left open at the end of a frame");
        Active = false;
    }
}
