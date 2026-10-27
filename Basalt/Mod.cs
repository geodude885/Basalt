using Basalt.Actors;
using Basalt.Battle;
using Basalt.Editing;
using Basalt.Menu;

namespace Basalt;

// The mod's services as ModEntry built them, for the DevTools, which cannot be handed anything.
internal static class Mod
{
    public static ActorsRegistry Actors { get; private set; } = null!;

    public static BattleController Battle { get; private set; } = null!;

    public static Studio Studio { get; private set; } = null!;

    public static ChartSelection Charts { get; private set; } = null!;

    public static BasaltMenu Menu { get; private set; } = null!;

    public static void Share(ActorsRegistry actors, BattleController battle, Studio studio, ChartSelection charts, BasaltMenu menu)
    {
        Actors = actors;
        Battle = battle;
        Studio = studio;
        Charts = charts;
        Menu = menu;
    }
}
