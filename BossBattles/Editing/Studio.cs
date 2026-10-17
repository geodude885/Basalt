using BossBattles.Battle;
using BossBattles.Services.Charts;
using BossBattles.Services.Clips;

namespace BossBattles.Editing;

internal class Studio
{
    public const double MaxRecordingLength = 600;
    public const double EaseInSeconds = 2;

    public ClipRecorder Recorder { get; private set; } = new();
    public ArenaKind? ArenaOverride { get; private set; }

    public Chart? Chart { get; private set; }
    public ChartEntry? Entry { get; private set; }
    public StudioState State { get; private set; } = StudioState.Off;
    public Clip? NewClip { get; private set; }
    public bool Mime { get; private set; }
    public RecordingSection SelectedSection { get; private set; }
    public LaneSetup LaneSetup { get; } = new();

    private Anchor? _recordedLane;
    private double _recordingStartedAtChartTime;
    private string _actorId = "";

    private RecordingSection? _sectionBeingRecorded;
    public bool IsSectionOrWholeClip => _sectionBeingRecorded != null;

    public string ActorName => Chart?.Actors.FirstOrDefault(a => a.Id == _actorId)?.Name ?? _actorId;
    public (double Seconds, int Moves) Progress => (Recorder.Length, Recorder.MoveCount);

    public static bool CanUndo(ChartEntry? entry) => entry is { CanEdit: true } && ChartBackup.HasBackup(entry.File);
    public static string Undo(ChartEntry entry) =>
        ChartBackup.RestoreBackup(entry.File) ? $"undid the last save to {entry.Name}" : $"{entry.Name} has nothing to undo";

    public void StartRecording(ChartEntry entry, ArenaKind? arenaOverride, bool mime = false)
    {
        if (!BeginEditing(entry, arenaOverride)) return;

        Mime = mime;
        StartStudioBattle(Chart!, BattleMode.Record, mime: mime);
        State = Mod.Battle.State == BattleState.Idle ? StudioState.Off : StudioState.Recording;
        Logger.Info($"[Studio] {(mime ? "miming" : "recording")} {ActorName} for {Chart!.Name} - STOP when done");
    }

    public void StopRecording()
    {
        if (State != StudioState.Recording) return;

        NewClip = Recorder.Stop(TrackEdits.NextName(ClipsFolder(), _actorId));
        _recordedLane = Mod.Battle.Lane ?? _recordedLane;
        Mod.Battle.Stop("recording stopped");

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
            var punchedIn = TrackEdits.PunchedInTrack(Chart, _actorId, NewClip.Name, section.From, section.To, Mime);
            var chart = Chart with { Tracks = TrackEdits.TracksWithActorTrackReplaced(Chart, _actorId, punchedIn) };
            StartStudioBattle(chart with { End = Math.Min(chart.End, section.To + EaseInSeconds) }, BattleMode.Review,
                startAt: Math.Max(0, section.From - EaseInSeconds), pendingClip: NewClip);
        }
        else
        {
            var whole = TrackEdits.WholeClipTrack(NewClip, _recordingStartedAtChartTime, Mime);
            var chart = Chart with
            {
                Tracks = TrackEdits.TracksWithActorTrackReplaced(Chart, _actorId, whole),
                End = Math.Max(Chart.End, _recordingStartedAtChartTime + NewClip.Duration + TrackEdits.EndPadding),
            };
            StartStudioBattle(chart, BattleMode.Review, lane: _recordedLane, pendingClip: NewClip);
        }

        State = StudioState.Reviewing;
    }

    public string KeepClip()
    {
        if (NewClip == null || Chart == null || Entry == null) return "no clip to keep";

        var clip = NewClip;
        Directory.CreateDirectory(ClipsFolder());
        File.WriteAllText(Path.Combine(ClipsFolder(), $"{clip.Name}.json"), ClipFile.Write(clip));

        var chartText = File.ReadAllText(Entry.File);
        var keptText = _sectionBeingRecorded is { } section
            ? TrackEdits.KeepSection(chartText, Chart, _actorId, clip, section.From, section.To, Mime)
            : TrackEdits.KeepWhole(chartText, Chart, _actorId, clip, _recordingStartedAtChartTime, Mime);

        if (BattlePlayer.Player?.Data?.PlayerMeasurement is { Length: > 0f } yours)
        {
            var size = new BodySize(yours.Length, yours.ArmSpan);
            keptText = ChartSetup.WriteActors(keptText, Chart.Actors.Select(a => a.Id == _actorId ? a with { Measurement = size } : a).ToList());
        }

        ChartBackup.SaveKeepingBackup(Entry.File, chartText, keptText);
        Mod.Battle.Stop("clip kept");

        var what = _sectionBeingRecorded is { } kept
            ? $"kept {clip.Name} over {kept.From:0.0}-{kept.To:0.0}s ({clip.Moves.Count} moves) in {Entry.Name}"
            : $"kept {clip.Name} ({clip.Duration - clip.Start:0.0}s, {clip.Moves.Count} moves) in {Entry.Name}";
        StopEditing();
        Logger.Debug($"[Studio.Kept] {what}");
        return what;
    }

    public void DiscardClip()
    {
        Mod.Battle.Stop("clip discarded");
        NewClip = null;
        if (IsSectionOrWholeClip) State = StudioState.ChoosingSection;
        else StopEditing();
    }

    public void RestartRecording()
    {
        if (Entry == null) return;
        if (IsSectionOrWholeClip) RecordSection(Mime);
        else StartRecording(Entry, ArenaOverride, Mime);
    }

    public void ChooseRecordingSection(ChartEntry entry, ArenaKind? arenaOverride)
    {
        if (!entry.HasActorTrack || !BeginEditing(entry, arenaOverride)) return;

        Mod.Battle.Stop("choosing a section");
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

        StartStudioBattle(Chart with { End = SelectedSection.To + 1 }, BattleMode.Review, startAt: Math.Max(0, SelectedSection.From - EaseInSeconds));
        State = StudioState.PreviewingSection;
    }

    public void RecordSection(bool mime = false)
    {
        if (Chart == null || Entry == null) return;

        _sectionBeingRecorded = SelectedSection;
        NewClip = null;
        Mime = mime;
        StartStudioBattle(Chart, BattleMode.Record, startAt: Math.Max(0, SelectedSection.From - EaseInSeconds), recordSpot: GetActorSpot(SelectedSection.From), mime: mime);
        State = Mod.Battle.State == BattleState.Idle ? StudioState.ChoosingSection : StudioState.Recording;
        Logger.Info($"[Studio] recording {ActorName} from {SelectedSection.From:0.0}s to {SelectedSection.To:0.0}s - it stops by itself");
    }

    public void CloseSection()
    {
        Mod.Battle.Stop("section closed");
        StopEditing();
    }

    public void OpenChartOptions(ChartEntry entry)
    {
        if (!BeginEditing(entry, null)) return;

        Mod.Battle.Stop("lane setup");
        LaneSetup.BeginWith(Chart!);
        State = StudioState.Setup;
    }

    public string SaveChartOptions()
    {
        if (Entry == null || Chart == null || State != StudioState.Setup) return "no lane setup to save";

        var chartText = File.ReadAllText(Entry.File);
        ChartBackup.SaveKeepingBackup(Entry.File, chartText, LaneSetup.WriteIntoChartText(chartText, Chart));
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
                if (Mod.Battle.Mode != BattleMode.Review || Mod.Battle.State is BattleState.Finished or BattleState.Idle) State = StudioState.Deciding;
                break;

            case StudioState.PreviewingSection:
                if (Mod.Battle.Mode != BattleMode.Review || Mod.Battle.State is BattleState.Finished or BattleState.Idle)
                {
                    Mod.Battle.Stop("section previewed");
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
        if (Mod.Battle.Mode != BattleMode.Record || Mod.Battle.State == BattleState.Idle)
        {
            Recorder.Cancel();
            StopEditing();
            return;
        }

        if (Mod.Battle.State != BattleState.Running) return;
        var chartTime = Mod.Battle.ChartTime;

        var startsAt = _sectionBeingRecorded?.From ?? 0;
        if (!Recorder.Recording && chartTime < startsAt)
        {
            Mod.Battle.Banner($"<b>{Math.Ceiling(startsAt - chartTime):0}</b>", 0.2f);
            return;
        }

        if (!Recorder.Recording)
        {
            if (BattlePlayer.Controller is not { } you || Mod.Battle.Lane is not { } lane) return;
            _recordedLane = lane;
            Recorder.Start(you, lane);
            _recordingStartedAtChartTime = chartTime;
            if (IsSectionOrWholeClip) Mod.Battle.Banner("<b>GO</b>", 0.8f);
        }

        Recorder.Update(chartTime);
        var sectionOver = _sectionBeingRecorded is { } section && chartTime >= section.To;
        if (sectionOver || Mod.Battle.SongOver || Recorder.Length > MaxRecordingLength) StopRecording();
    }

    private bool BeginEditing(ChartEntry entry, ArenaKind? arenaOverride)
    {
        if (!entry.CanEdit) return false;

        Recorder.Cancel();
        StopEditing();
        Entry = entry;
        Chart = entry.Result.Chart!;
        ArenaOverride = arenaOverride;
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

    private void StartStudioBattle(
        Chart chart,
        BattleMode mode,
        double startAt = 0,
        Anchor? lane = null,
        Clip? pendingClip = null,
        System.Numerics.Vector2? recordSpot = null,
        bool mime = false) => Mod.Battle.Start(chart, ArenaOverride, Entry!.AssetFolder, scoreKey: null, mode, _actorId, lane, pendingClip, startAt, recordSpot, mime);

    private System.Numerics.Vector2? GetActorSpot(double chartTime)
    {
        if (Chart == null || Entry == null) return null;

        var playing = new ChartRunner(Chart, chartTime).Schedule.OfType<PlayClip>().FirstOrDefault(t => t.ActorId == _actorId && t.Time <= chartTime);
        if (playing == null) return null;

        var clips = new ClipLibrary(ClipsFolder());
        clips.Reload();
        return clips.Find(playing.Clip)?.Sample((float)playing.From).Offset;
    }

    private string ClipsFolder() => Path.Combine(Entry!.AssetFolder, ChartPackage.ClipsFolder);
}
