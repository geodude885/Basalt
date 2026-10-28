using Basalt.Battle;
using Basalt.Presentation;
using Basalt.Services.Charts;
using Basalt.Services.Clips;

namespace Basalt.Editing;

internal enum StudioState
{
    Off,
    Setup,
    Recording,
    Reviewing,
    Deciding,
}


internal class Studio(BattleController battle, GameAssets assets)
{
    public const double MaxRecordingLength = 600;

    public ClipRecorder Recorder { get; private set; } = new();

    public Chart? Chart { get; private set; }
    public ChartEntry? Entry { get; private set; }
    public StudioState State { get; private set; } = StudioState.Off;
    public Clip? NewClip { get; private set; }
    public bool Mime { get; private set; }
    public LaneSetup LaneSetup { get; } = new(assets);

    private Anchor? _recordedLane;
    private double _recordingStartedAtChartTime;
    private string _actorId = "";

    public string ActorName => Chart?.Actors.FirstOrDefault(a => a.Id == _actorId)?.Name ?? _actorId;
    public (double Seconds, int Moves) Progress => (Recorder.Length, Recorder.MoveCount);

    public static bool CanUndo(ChartEntry? entry) => entry is { CanEdit: true } && ChartFiles.HasBackup(entry.File);
    public static string Undo(ChartEntry entry) =>
        ChartFiles.RestoreBackup(entry.File) ? $"undid the last save to {entry.Name}" : $"{entry.Name} has nothing to undo";

    public void StartRecording(ChartEntry entry, string? actorId = null, bool mime = false)
    {
        if (!BeginEditing(entry, actorId)) return;

        Mime = mime;
        battle.Start(StudioBattle(Chart!) with { Mode = BattleMode.Record, Mime = mime });
        State = battle.State == BattleState.Idle ? StudioState.Off : StudioState.Recording;
        Logger.Info($"[Studio] {(mime ? "miming" : "recording")} {ActorName} for {Chart!.Name} - STOP when done");
    }

    public void StopRecording()
    {
        if (State != StudioState.Recording) return;

        NewClip = Recorder.Stop(ChartClips().NextName(_actorId));
        _recordedLane = battle.Lane ?? _recordedLane;
        battle.Stop("recording stopped");

        if (NewClip == null)
        {
            State = StudioState.Off;
            Logger.Info("[Studio] nothing was recorded");
            return;
        }

        ReviewRecording();
    }

    public void ReviewRecording()
    {
        if (NewClip == null || Chart == null || Entry == null) return;

        var whole = TrackWriter.WholeClipTrack(NewClip, _recordingStartedAtChartTime, Mime);
        var chart = Chart with
        {
            Tracks = TrackWriter.TracksWithActorTrackReplaced(Chart, _actorId, whole),
            End = Math.Max(Chart.End, _recordingStartedAtChartTime + NewClip.Duration + TrackWriter.EndPadding),
        };
        battle.Start(StudioBattle(chart) with { Mode = BattleMode.Review, Lane = _recordedLane, PendingClip = NewClip });
        State = StudioState.Reviewing;
    }

    public string KeepClip()
    {
        if (NewClip == null || Chart == null || Entry == null) return "no clip to keep";

        var clip = NewClip;
        ChartClips().Save(clip);

        var chartText = File.ReadAllText(Entry.File);
        var keptText = TrackWriter.KeepWhole(chartText, Chart, _actorId, clip, _recordingStartedAtChartTime, Mime);

        if (BattlePlayer.Player?.Data?.PlayerMeasurement is { Length: > 0f } playerMeasurement)
        {
            var size = new BodySize(playerMeasurement.Length, playerMeasurement.ArmSpan);
            keptText = LaneSetupWriter.WriteActors(keptText, Chart.Actors.Select(a => a.Id == _actorId ? a with { Measurement = size } : a).ToList());
        }

        ChartFiles.SaveKeepingBackup(Entry.File, chartText, keptText);
        battle.Stop("clip kept");

        var what = $"kept {clip.Name} ({clip.Duration - clip.Start:0.0}s, {clip.Moves.Count} moves) in {Entry.Name}";
        StopEditing();
        Logger.Debug($"[Studio.Kept] {what}");
        return what;
    }

    public void DiscardClip()
    {
        battle.Stop("clip discarded");
        StopEditing();
    }

    public void RestartRecording()
    {
        if (Entry != null) StartRecording(Entry, _actorId, Mime);
    }

    public void OpenChartOptions(ChartEntry entry)
    {
        if (!BeginEditing(entry)) return;

        battle.Stop("lane setup");
        LaneSetup.BeginWith(Chart!);
        State = StudioState.Setup;
    }

    public string SaveChartOptions()
    {
        if (Entry == null || Chart == null || State != StudioState.Setup) return "no lane setup to save";

        var chartText = File.ReadAllText(Entry.File);
        ChartFiles.SaveKeepingBackup(Entry.File, chartText, LaneSetup.WriteIntoChartText(chartText, Chart));
        ExitChartOptions();
        return $"saved {Entry.Name}'s lane";
    }

    public void ExitChartOptions()
    {
        LaneSetup.RemovePreview();
        StopEditing();
    }

    public void Update()
    {
        switch (State)
        {
            case StudioState.Recording:
                UpdateRecording();
                break;

            case StudioState.Reviewing:
                if (battle.Mode != BattleMode.Review || battle.BetweenBattles) State = StudioState.Deciding;
                break;

            case StudioState.Setup:
                LaneSetup.RebuildPreviewIfOutOfDate();
                break;
        }
    }

    public void OnSceneLoaded() => LaneSetup.ForgetPreviewAfterSceneChange();

    private void UpdateRecording()
    {
        if (battle.Mode != BattleMode.Record || battle.State == BattleState.Idle)
        {
            Recorder.Cancel();
            StopEditing();
            return;
        }

        if (battle.State != BattleState.Running) return;
        var chartTime = battle.ChartTime;

        if (!Recorder.Recording)
        {
            if (BattlePlayer.Player?.Controller is not { } playerController || battle.Lane is not { } lane) return;
            _recordedLane = lane;
            Recorder.Start(playerController, lane);
            _recordingStartedAtChartTime = chartTime;
        }

        Recorder.Update(chartTime);
        if (battle.SongOver || Recorder.Length > MaxRecordingLength) StopRecording();
    }

    private bool BeginEditing(ChartEntry entry, string? actorId = null)
    {
        if (!entry.CanEdit) return false;

        Recorder.Cancel();
        StopEditing();
        Entry = entry;
        Chart = entry.Result.Chart!;
        _actorId = Chart.Actors.FirstOrDefault(a => a.Id == actorId)?.Id ?? Chart.DefaultActor.Id;
        _recordedLane = null;
        return true;
    }

    private void StopEditing()
    {
        State = StudioState.Off;
        NewClip = null;
    }

    private BattleRequest StudioBattle(Chart chart) => new(chart) { AssetFolder = Entry!.AssetFolder, RecordActor = _actorId };

    private ClipLibrary ChartClips() => new(Path.Combine(Entry!.AssetFolder, ChartPackage.ClipsFolder));
}
