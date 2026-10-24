using Basalt.Battle;
using Basalt.Presentation;
using Basalt.Services.Charts;
using Basalt.Services.Clips;

namespace Basalt.Editing;

internal class Studio(BattleController battle, GameAssets assets)
{
    public const double MaxRecordingLength = 600;
    public const double EaseInSeconds = 2;

    public ClipRecorder Recorder { get; private set; } = new();

    public Chart? Chart { get; private set; }
    public ChartEntry? Entry { get; private set; }
    public StudioState State { get; private set; } = StudioState.Off;
    public Clip? NewClip { get; private set; }
    public bool Mime { get; private set; }
    public RecordingSection SelectedSection { get; private set; }
    public LaneSetup LaneSetup { get; } = new(assets);

    private Anchor? _recordedLane;
    private double _recordingStartedAtChartTime;
    private string _actorId = "";

    private RecordingSection? _sectionBeingRecorded;
    public bool IsSectionOrWholeClip => _sectionBeingRecorded != null;

    public string ActorName => Chart?.Actors.FirstOrDefault(a => a.Id == _actorId)?.Name ?? _actorId;
    public (double Seconds, int Moves) Progress => (Recorder.Length, Recorder.MoveCount);

    public static bool CanUndo(ChartEntry? entry) => entry is { CanEdit: true } && ChartFiles.HasBackup(entry.File);
    public static string Undo(ChartEntry entry) =>
        ChartFiles.RestoreBackup(entry.File) ? $"undid the last save to {entry.Name}" : $"{entry.Name} has nothing to undo";

    public void StartRecording(ChartEntry entry, bool mime = false)
    {
        if (!BeginEditing(entry)) return;

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
            State = IsSectionOrWholeClip ? StudioState.ChoosingSection : StudioState.Off;
            Logger.Info("[Studio] nothing was recorded");
            return;
        }

        if (_sectionBeingRecorded is { } section)
            _sectionBeingRecorded = section with { To = Math.Min(section.To, _recordingStartedAtChartTime + NewClip.Duration) };

        ReviewRecording();
    }

    public void ReviewRecording()
    {
        if (NewClip == null || Chart == null || Entry == null) return;

        if (_sectionBeingRecorded is { } section)
        {
            var punchedIn = TrackWriter.PunchedInTrack(Chart, _actorId, NewClip.Name, section.From, section.To, Mime);
            var chart = Chart with { Tracks = TrackWriter.TracksWithActorTrackReplaced(Chart, _actorId, punchedIn) };
            battle.Start(StudioBattle(chart with { End = Math.Min(chart.End, section.To + EaseInSeconds) }) with
            {
                Mode = BattleMode.Review,
                StartAt = section.From - EaseInSeconds,
                PendingClip = NewClip,
            });
        }
        else
        {
            var whole = TrackWriter.WholeClipTrack(NewClip, _recordingStartedAtChartTime, Mime);
            var chart = Chart with
            {
                Tracks = TrackWriter.TracksWithActorTrackReplaced(Chart, _actorId, whole),
                End = Math.Max(Chart.End, _recordingStartedAtChartTime + NewClip.Duration + TrackWriter.EndPadding),
            };
            battle.Start(StudioBattle(chart) with { Mode = BattleMode.Review, Lane = _recordedLane, PendingClip = NewClip });
        }

        State = StudioState.Reviewing;
    }

    public string KeepClip()
    {
        if (NewClip == null || Chart == null || Entry == null) return "no clip to keep";

        var clip = NewClip;
        ChartClips().Save(clip);

        var chartText = File.ReadAllText(Entry.File);
        var keptText = _sectionBeingRecorded is { } section
            ? TrackWriter.KeepSection(chartText, Chart, _actorId, clip, section.From, section.To, Mime)
            : TrackWriter.KeepWhole(chartText, Chart, _actorId, clip, _recordingStartedAtChartTime, Mime);

        if (BattlePlayer.Player?.Data?.PlayerMeasurement is { Length: > 0f } playerMeasurement)
        {
            var size = new BodySize(playerMeasurement.Length, playerMeasurement.ArmSpan);
            keptText = LaneSetupWriter.WriteActors(keptText, Chart.Actors.Select(a => a.Id == _actorId ? a with { Measurement = size } : a).ToList());
        }

        ChartFiles.SaveKeepingBackup(Entry.File, chartText, keptText);
        battle.Stop("clip kept");

        var what = _sectionBeingRecorded is { } kept
            ? $"kept {clip.Name} over {kept.From:0.0}-{kept.To:0.0}s ({clip.Moves.Count} moves) in {Entry.Name}"
            : $"kept {clip.Name} ({clip.Duration - clip.Start:0.0}s, {clip.Moves.Count} moves) in {Entry.Name}";
        StopEditing();
        Logger.Debug($"[Studio.Kept] {what}");
        return what;
    }

    public void DiscardClip()
    {
        battle.Stop("clip discarded");
        NewClip = null;
        if (IsSectionOrWholeClip) State = StudioState.ChoosingSection;
        else StopEditing();
    }

    public void RestartRecording()
    {
        if (Entry == null) return;
        if (IsSectionOrWholeClip) RecordSection(Mime);
        else StartRecording(Entry, Mime);
    }

    public void ChooseRecordingSection(ChartEntry entry)
    {
        if (!entry.HasActorTrack || !BeginEditing(entry)) return;

        battle.Stop("choosing a section");
        SelectedSection = RecordingSection.MiddleThirdOf(Chart!);
        State = StudioState.ChoosingSection;
    }

    public void NudgeRecordingSection(bool start, int beats)
    {
        if (Chart == null) return;
        SelectedSection = start ? SelectedSection.MoveStartByBeats(Chart, beats) : SelectedSection.MoveEndByBeats(Chart, beats);
    }

    public void PreviewSelectedSection()
    {
        if (Chart == null || Entry == null) return;

        battle.Start(StudioBattle(Chart with { End = SelectedSection.To + 1 }) with { Mode = BattleMode.Review, StartAt = SelectedSection.From - EaseInSeconds });
        State = StudioState.PreviewingSection;
    }

    public void RecordSection(bool mime = false)
    {
        if (Chart == null || Entry == null) return;

        _sectionBeingRecorded = SelectedSection;
        NewClip = null;
        Mime = mime;
        battle.Start(StudioBattle(Chart) with
        {
            Mode = BattleMode.Record,
            StartAt = SelectedSection.From - EaseInSeconds,
            RecordSpot = GetActorSpot(SelectedSection.From),
            Mime = mime,
        });
        State = battle.State == BattleState.Idle ? StudioState.ChoosingSection : StudioState.Recording;
        Logger.Info($"[Studio] recording {ActorName} from {SelectedSection.From:0.0}s to {SelectedSection.To:0.0}s - it stops by itself");
    }

    public void CloseSection()
    {
        battle.Stop("section closed");
        StopEditing();
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

            case StudioState.PreviewingSection:
                if (battle.Mode != BattleMode.Review || battle.BetweenBattles)
                {
                    battle.Stop("section previewed");
                    State = StudioState.ChoosingSection;
                }

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

        var startsAt = _sectionBeingRecorded?.From ?? 0;
        if (!Recorder.Recording && chartTime < startsAt)
        {
            battle.Banner($"{Math.Ceiling(startsAt - chartTime):0}", 0.2f);
            return;
        }

        if (!Recorder.Recording)
        {
            if (BattlePlayer.Controller is not { } playerController || battle.Lane is not { } lane) return;
            _recordedLane = lane;
            Recorder.Start(playerController, lane);
            _recordingStartedAtChartTime = chartTime;
            if (IsSectionOrWholeClip) battle.Banner("GO", 0.8f);
        }

        Recorder.Update(chartTime);
        var sectionOver = _sectionBeingRecorded is { } section && chartTime >= section.To;
        if (sectionOver || battle.SongOver || Recorder.Length > MaxRecordingLength) StopRecording();
    }

    private bool BeginEditing(ChartEntry entry)
    {
        if (!entry.CanEdit) return false;

        Recorder.Cancel();
        StopEditing();
        Entry = entry;
        Chart = entry.Result.Chart!;
        _actorId = Chart.DefaultActor.Id;
        _recordedLane = null;
        return true;
    }

    private void StopEditing()
    {
        State = StudioState.Off;
        NewClip = null;
        _sectionBeingRecorded = null;
    }

    private BattleRequest StudioBattle(Chart chart) => new(chart) { AssetFolder = Entry!.AssetFolder, RecordActor = _actorId };

    private System.Numerics.Vector2? GetActorSpot(double chartTime)
    {
        if (Chart == null || Entry == null) return null;

        var playing = new ChartRunner(Chart, chartTime).Schedule.OfType<PlayClip>().FirstOrDefault(t => t.ActorId == _actorId && t.Time <= chartTime);
        if (playing == null) return null;

        var clips = ChartClips();
        clips.Reload();
        return clips.Find(playing.Clip)?.Sample((float)playing.From).Offset;
    }

    private ClipLibrary ChartClips() => new(Path.Combine(Entry!.AssetFolder, ChartPackage.ClipsFolder));
}
