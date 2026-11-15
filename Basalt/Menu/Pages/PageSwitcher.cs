using Basalt.Arenas;
using Basalt.Battle;
using Basalt.Editing;
using Basalt.Services;
using Basalt.Services.Charts;
using Basalt.Services.Scoring;

namespace Basalt.Menu.Pages;

internal class PageSwitcher(ChartSelection charts, ChartLibrary library, JsonFileRepository<BestScore> scores, Settings settings, BattleController battle, Studio studio)
{
    private readonly ChartsPage _charts = new(charts, library, scores, settings, battle, studio);
    private readonly RecordingPage _recording = new(studio);
    private readonly ReviewRecordingPage _review = new(charts, studio);
    private readonly LaneSetupPage _laneSetup = new(charts, studio);
    private readonly FlashingNoticePage _flashingNotice = new(charts);

    public IMenuScreen? Current => studio.State switch
    {
        StudioState.Recording => battle.Run?.State == BattleState.Running ? _recording : null,
        StudioState.Setup => _laneSetup,
        StudioState.Reviewing => null,
        StudioState.Deciding => NoBattleRunning ? _review : null,
        _ => ChartPickerOpen ? ChartPicking : null,
    };

    private bool NoBattleRunning => battle.Run?.State is null or BattleState.Finished;

    private bool ChartPickerOpen => !Maps.IsCurrentlyOnline && battle.CanPickChartsHere && NoBattleRunning;

    private IMenuScreen ChartPicking
    {
        get
        {
            if (charts.AskingAboutFlashing) return _flashingNotice;
            if (_charts.Guide.IsOpen) return _charts.Guide;
            return _charts;
        }
    }
}
