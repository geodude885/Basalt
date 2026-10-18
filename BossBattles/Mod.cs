using BossBattles.Actors;
using BossBattles.Battle;
using BossBattles.Editing;
using BossBattles.UI;

namespace BossBattles;

// The mod's services as ModEntry built them, for the DevTools, which cannot be handed anything.
internal static class Mod
{
    public static ActorRegistry Actors { get; private set; } = null!;

    public static BattleController Battle { get; private set; } = null!;

    public static Studio Studio { get; private set; } = null!;

    public static ChartSelection Charts { get; private set; } = null!;

    public static ChartPicker Picker { get; private set; } = null!;

    public static void Share(ActorRegistry actors, BattleController battle, Studio studio, ChartSelection charts, ChartPicker picker)
    {
        Actors = actors;
        Battle = battle;
        Studio = studio;
        Charts = charts;
        Picker = picker;
    }
}
