using Basalt.Arenas;
using Basalt.Battle;
using Basalt.Editing;
using Basalt.Services;
using Basalt.Services.Charts;
using Basalt.Services.Scoring;

namespace Basalt.Menu.Pages;

internal class PageSwitcher(ChartSelection charts, ChartLibrary library, JsonBook<BestScore> scores, Settings settings, BattleController battle, Studio studio)
{
    private readonly ChartsPage _charts = new(charts, library, scores, settings, battle, studio);
    private readonly RecordingPage _recording = new(studio);
    private readonly ReviewRecordingPage _review = new(charts, studio);
    private readonly LaneSetupPage _laneSetup = new(charts, studio);

    public IMenuScreen? Current => studio.State switch
    {
        StudioState.Recording => battle.Run?.State == BattleState.Running ? _recording : null,
        StudioState.Setup => _laneSetup,
        StudioState.Reviewing => null,
        StudioState.Deciding => battle.Run?.State is null or BattleState.Finished ? _review : null,
        _ => !Maps.IsCurrentlyOnline && battle.Run?.State is null or BattleState.Finished ? _charts : null,
    };
}
