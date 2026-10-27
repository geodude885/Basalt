using Basalt.Arenas;
using Basalt.Battle;
using Basalt.Editing;
using Basalt.Services;
using Basalt.Services.Charts;
using Basalt.Services.Scoring;

namespace Basalt.Menu.Screens;

internal class MenuScreens(ChartSelection charts, ChartLibrary library, JsonBook<BestScore> scores, Settings settings, BattleController battle, Studio studio)
{
    private readonly ChartsScreen _charts = new(charts, library, scores, settings, battle, studio);
    private readonly RecordingScreen _recording = new(studio);
    private readonly ReviewScreen _review = new(charts, studio);
    private readonly LaneSetupScreen _laneSetup = new(charts, studio);

    // The screen for the moment; none while a battle plays or a clip is replayed.
    public IMenuScreen? Current => studio.State switch
    {
        StudioState.Recording => battle.State == BattleState.Running ? _recording : null,
        StudioState.Setup => _laneSetup,
        StudioState.Reviewing => null,
        StudioState.Deciding => battle.BetweenBattles ? _review : null,
        _ => !Maps.IsCurrentlyOnline && battle.BetweenBattles ? _charts : null,
    };
}
