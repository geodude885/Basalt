using BossBattles.Battle;
using BossBattles.Services.Charts;
using BossBattles.Services.Clips;
using UnityEngine;

namespace BossBattles.Authoring;

internal enum StudioState
{
    Off,
    Setup,
    ChoosingSection,
    PreviewingSection,
    Recording,
    Reviewing,
    Deciding,
}

/// <summary>
/// Chart editing in the game: recording an actor's performance in its place, on the chart's clock,
/// then watching it performed at you before keeping it into the chart's <c>clips/</c> and
/// <c>tracks</c>; a section can be recorded again and spliced in. Each save keeps the previous chart
/// in <c>chart.json.bak</c> for <see cref="Undo"/>, and only the edited values of its text change.
/// </summary>
internal partial class Studio
{
    public const double MaxRecordingLength = 600;
    public const double EaseInSeconds = 2;
    private const string BackupExtension = ".bak";

    public Chart? Chart { get; private set; }
    public StudioState State { get; private set; } = StudioState.Off;
    public Clip? NewClip { get; private set; }
    public ChartEntry? Entry { get; private set; }

    public (double From, double To) Section { get; private set; }

    public bool IsSection => _section != null;

    public string ActorName => Chart?.Actors.FirstOrDefault(a => a.Id == _actorId)?.Name ?? _actorId;

    public (double Seconds, int Moves) Progress => (_recorder.Length, _recorder.MoveCount);


    public static bool CanEdit(ChartEntry? entry) => entry?.Result.Chart != null && entry.Source != ChartSource.Package;

    public static bool HasTrack(ChartEntry? entry) =>
        CanEdit(entry) && entry!.Result.Chart!.Tracks.Any(t => t.ActorId == entry.Result.Chart.DefaultActor.Id && t.Clips.Count > 0);

    public static bool CanUndo(ChartEntry? entry) => CanEdit(entry) && File.Exists(entry!.File + BackupExtension);

    private readonly ClipRecorder _recorder = new();
    private double _chartTime;
    private readonly Arena.Arena _preview = new();
    private readonly List<GameObject> _markers = [];
    private ArenaKind? _arenaOverride;
    private Anchor? _lane;
    private string _actorId = "";

    private (double From, double To)? _section;

    public void StartRecording(ChartEntry entry, ArenaKind? arenaOverride, bool mime = false)
    {
        if (!Begin(entry, arenaOverride)) return;

        _section = null;
        Mime = mime;
        Mod.Battle.Start(Chart!, arenaOverride, entry.AssetFolder, scoreKey: null, BattleMode.Record, _actorId, mime: mime);
        State = Mod.Battle.State == BattleState.Idle ? StudioState.Off : StudioState.Recording;
        Logger.Info($"[Studio] {(mime ? "miming" : "recording")} {ActorName} for {Chart!.Name} - STOP when done");
    }

    public bool Mime { get; private set; }

    public void StopRecording()
    {
        if (State != StudioState.Recording) return;

        var name = TrackEdits.NextName(ClipsFolder(), _actorId);
        NewClip = _recorder.Stop(name);
        _lane = Mod.Battle.Lane ?? _lane;
        Mod.Battle.Stop("recording stopped");

        if (NewClip == null)
        {
            State = _section != null ? StudioState.ChoosingSection : StudioState.Off;
            Logger.Info("[Studio] nothing was recorded");
            return;
        }

        if (_section is { } section) _section = (section.From, Math.Min(section.To, _chartTime + NewClip.Duration));
        ReviewRecording();
    }

    public void ReviewRecording()
    {
        if (NewClip == null || Chart == null || Entry == null) return;

        if (_section is { } section)
        {
            var before = Chart.Tracks.FirstOrDefault(t => t.ActorId == _actorId)?.Clips ?? Array.Empty<TrackClip>();
            var spliceTracks = Chart.Tracks.Where(t => t.ActorId != _actorId).ToList();
            spliceTracks.Add(new Track(_actorId, TrackEdits.PunchIn(before, NewClip.Name, section.From, section.To, Mime)));
            var spliced = Chart with { Tracks = spliceTracks };
            Mod.Battle.Start(spliced with { End = Math.Min(spliced.End, section.To + EaseInSeconds) }, _arenaOverride, Entry.AssetFolder,
                scoreKey: null, BattleMode.Review, _actorId, pendingClip: NewClip, startAt: Math.Max(0, section.From - EaseInSeconds));
        }
        else
        {
            var tracks = Chart.Tracks.Where(t => t.ActorId != _actorId).ToList();
            tracks.Add(new Track(_actorId, new[] { new TrackClip(_chartTime, NewClip.Name, 0) { Mime = Mime } }));
            var chart = Chart with { Tracks = tracks, End = Math.Max(Chart.End, _chartTime + NewClip.Duration + TrackEdits.EndPadding) };
            Mod.Battle.Start(chart, _arenaOverride, Entry.AssetFolder, scoreKey: null, BattleMode.Review, _actorId, _lane, NewClip);
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
        var kept = _section is { } section
            ? TrackEdits.KeepSection(chartText, Chart, _actorId, clip, section.From, section.To, Mime)
            : TrackEdits.KeepWhole(chartText, Chart, _actorId, clip, _chartTime, Mime);

        if (BattlePlayer.Player?.Data?.PlayerMeasurement is { Length: > 0f } yours)
        {
            var size = new BodySize(yours.Length, yours.ArmSpan);
            kept = ChartSetup.WriteActors(kept, Chart.Actors.Select(a => a.Id == _actorId ? a with { Measurement = size } : a).ToList());
        }

        Save(chartText, kept);

        Mod.Battle.Stop("clip kept");
        var what = _section is { } s
            ? $"kept {clip.Name} over {s.From:0.0}-{s.To:0.0}s ({clip.Moves.Count} moves) in {Entry.Name}"
            : $"kept {clip.Name} ({clip.Duration - clip.Start:0.0}s, {clip.Moves.Count} moves) in {Entry.Name}";
        Off();
        Logger.Debug($"[Studio.Kept] {what}");
        return what;
    }

    public void DiscardClip()
    {
        Mod.Battle.Stop("clip discarded");
        NewClip = null;
        if (_section != null) State = StudioState.ChoosingSection;
        else Off();
    }

    public void RestartRecording()
    {
        if (Entry == null) return;
        if (_section != null) RecordSection(Mime);
        else StartRecording(Entry, _arenaOverride, Mime);
    }


    public void ChooseRecordingSection(ChartEntry entry, ArenaKind? arenaOverride)
    {
        if (!HasTrack(entry) || !Begin(entry, arenaOverride)) return;

        Mod.Battle.Stop("choosing a section");

        var end = Chart!.End;
        Section = (Snap(end / 3), Snap(end * 2 / 3));
        if (Section.To <= Section.From) Section = (Section.From, Section.From + Step);
        State = StudioState.ChoosingSection;
    }

    public void NudgeRecordingSection(bool start, int steps)
    {
        if (Chart == null) return;

        double from = Section.From, to = Section.To;
        if (start) from = Math.Clamp(from + (steps * Step), 0, to - Step);
        else to = Math.Clamp(to + (steps * Step), from + Step, Chart.End + 30);

        Section = (Snap(from), Snap(to));
    }

    public void PreviewSelectedSection()
    {
        if (Chart == null || Entry == null) return;

        Mod.Battle.Start(Chart with { End = Section.To + 1 }, _arenaOverride, Entry.AssetFolder, scoreKey: null, BattleMode.Review, _actorId,
            startAt: Math.Max(0, Section.From - EaseInSeconds));
        State = StudioState.PreviewingSection;
    }

    public void RecordSection(bool mime = false)
    {
        if (Chart == null || Entry == null) return;

        _section = Section;
        NewClip = null;
        Mime = mime;
        Mod.Battle.Start(Chart, _arenaOverride, Entry.AssetFolder, scoreKey: null, BattleMode.Record, _actorId,
            startAt: Math.Max(0, Section.From - EaseInSeconds), recordSpot: SpotAt(Section.From), mime: mime);
        State = Mod.Battle.State == BattleState.Idle ? StudioState.ChoosingSection : StudioState.Recording;
        Logger.Info($"[Studio] recording {ActorName} from {Section.From:0.0}s to {Section.To:0.0}s - it stops by itself");
    }

    public void Back()
    {
        Mod.Battle.Stop("section closed");
        Off();
    }

    public static string Undo(ChartEntry entry)
    {
        var backup = entry.File + BackupExtension;
        if (!File.Exists(backup)) return $"{entry.Name} has nothing to undo";

        File.Copy(backup, entry.File, overwrite: true);
        File.Delete(backup);
        return $"undid the last save to {entry.Name}";
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
                if (_setupDirty) BuildPreview();
                break;
        }
    }

    public void OnSceneLoaded()
    {
        _preview.Forget();
        _markers.Clear();
        _setupDirty = true;
    }

    private void UpdateRecording()
    {
        if (Mod.Battle.Mode != BattleMode.Record || Mod.Battle.State == BattleState.Idle)
        {
            _recorder.Cancel();
            Off();
            return;
        }

        if (Mod.Battle.State != BattleState.Running) return;
        var now = Mod.Battle.ChartTime;

        var from = _section?.From ?? 0;
        if (!_recorder.Recording && now < from)
        {
            Mod.Battle.Banner($"<b>{Math.Ceiling(from - now):0}</b>", 0.2f);
            return;
        }

        if (!_recorder.Recording)
        {
            if (BattlePlayer.Controller is not { } you || Mod.Battle.Lane is not { } lane) return;
            _lane = lane;
            _recorder.Start(you, lane);
            _chartTime = now;
            if (_section != null) Mod.Battle.Banner("<b>GO</b>", 0.8f);
        }

        _recorder.Update(now);
        var sectionOver = _section is { } section && now >= section.To;
        if (sectionOver || Mod.Battle.SongOver || _recorder.Length > MaxRecordingLength) StopRecording();
    }

    private bool Begin(ChartEntry entry, ArenaKind? arenaOverride)
    {
        if (!CanEdit(entry)) return false;

        _recorder.Cancel();
        Entry = entry;
        Chart = entry.Result.Chart!;
        _arenaOverride = arenaOverride;
        _actorId = Chart.DefaultActor.Id;
        _lane = null;
        _section = null;
        NewClip = null;
        return true;
    }

    private void Off()
    {
        State = StudioState.Off;
        NewClip = null;
        _section = null;
    }

    /// <summary>Where the actor stood at chart time <paramref name="time"/>, from the clip playing then.</summary>
    private System.Numerics.Vector2? SpotAt(double time)
    {
        if (Chart == null || Entry == null) return null;

        var playing = new ChartRunner(Chart, time).Schedule.OfType<PlayClip>().FirstOrDefault(t => t.ActorId == _actorId && t.Time <= time);
        if (playing == null) return null;

        var clips = new ClipLibrary(ClipsFolder());
        clips.Reload();
        return clips.Find(playing.Clip)?.Sample((float)playing.From).Offset;
    }

    private void Save(string before, string after)
    {
        File.WriteAllText(Entry!.File + BackupExtension, before);
        File.WriteAllText(Entry.File, after);
    }

    private double Step => Chart?.Tempo is { } tempo ? 60.0 / tempo.BpmAt(Section.From) : 0.5;

    /// <summary>To the nearest beat through tempo changes, or half second without a tempo.</summary>
    private double Snap(double seconds) => Chart?.Tempo is { } tempo
        ? Math.Round(tempo.Seconds(Math.Round(tempo.Beat(seconds))), 3)
        : Math.Round(Math.Round(seconds / 0.5) * 0.5, 3);


    private string ClipsFolder() => Path.Combine(Entry!.AssetFolder, ChartPackage.ClipsFolder);
}
