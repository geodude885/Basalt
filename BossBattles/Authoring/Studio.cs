using BossBattles.Actors;
using BossBattles.Arenas;
using BossBattles.Battle;
using BossBattles.Services.Charts;
using BossBattles.Services.Clips;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;
using UnityEngine;

namespace BossBattles.Authoring;

internal enum StudioState
{
    /// <summary>Not editing: battles are battles.</summary>
    Off,

    /// <summary>Setting the lane up: a preview lane you walk around, resized and moved from the panel.</summary>
    Setup,

    /// <summary>Choosing a section of the actor's track to record again.</summary>
    ChoosingSection,

    /// <summary>Playing a section as it is, from a little before it.</summary>
    PreviewingSection,

    /// <summary>You stand in the actor's place; the clip (or section) is being recorded.</summary>
    Recording,

    /// <summary>The actor performs the clip at you, as a player will see it.</summary>
    Reviewing,

    /// <summary>The clip waits for keep, again, replay or discard.</summary>
    Deciding,
}

/// <summary>
/// Chart editing in the game: recording an actor's performance in its place, on the chart's clock,
/// then watching it performed at you before keeping it into the chart's <c>clips/</c> and
/// <c>tracks</c>; a section can be recorded again and spliced in. Each save keeps the previous chart
/// in <c>chart.json.bak</c> for <see cref="Undo"/>, and only the edited values of its text change.
/// </summary>
internal sealed partial class Studio
{
    /// <summary>A recording stops by itself after this long.</summary>
    public const double MaxSeconds = 600;

    /// <summary>A section is recorded, previewed and reviewed from this long before it.</summary>
    public const double PrerollSeconds = 2;

    private const string BackupExtension = ".bak";

    private readonly ClipRecorder _recorder = new();
    /// <summary>Chart time the recording began: the clip's time 0.</summary>
    private double _recordedAt;
    private readonly LaneVisuals _preview = new();
    private readonly List<GameObject> _markers = [];
    private ArenaKind? _arenaOverride;
    private LaneFrame? _lane;
    private string _actorId = "";

    /// <summary>A section being recorded again; null while recording whole.</summary>
    private (double From, double To)? _section;


    public StudioState State { get; private set; } = StudioState.Off;

    public Clip? NewClip { get; private set; }

    public ChartEntry? Entry { get; private set; }

    public (double From, double To) Section { get; private set; }

    public bool IsSection => _section != null;

    public string ActorName => Chart?.Actors.FirstOrDefault(a => a.Id == _actorId)?.Name ?? _actorId;

    public (double Seconds, int Moves) Progress => (_recorder.Length, _recorder.MoveCount);

    public Chart? Chart { get; private set; }

    /// <summary>A chart can be edited when it loads and is not a (read-only) package.</summary>
    public static bool CanEdit(ChartEntry? entry) => entry?.Result.Chart != null && entry.Source != ChartSource.Package;

    public static bool HasTrack(ChartEntry? entry) =>
        CanEdit(entry) && entry!.Result.Chart!.Tracks.Any(t => t.ActorId == entry.Result.Chart.DefaultActor.Id && t.Clips.Count > 0);

    public static bool CanUndo(ChartEntry? entry) => CanEdit(entry) && File.Exists(entry!.File + BackupExtension);

    /// <summary>
    /// Starts recording the chart's first actor. Mimed, the chart's projectiles fly as you perform,
    /// nothing you do spawns, and the kept clip runs no moves.
    /// </summary>
    public void Record(ChartEntry entry, ArenaKind? arenaOverride, bool mime = false)
    {
        if (!Begin(entry, arenaOverride)) return;

        _section = null;
        Mime = mime;
        Mod.Battle.Start(Chart!, arenaOverride, entry.AssetFolder, scoreKey: null, BattleMode.Record, _actorId, mime: mime);
        State = Mod.Battle.State == BattleState.Idle ? StudioState.Off : StudioState.Recording;
        Logger.Info($"[Studio] {(mime ? "miming" : "recording")} {ActorName} for {Chart!.Name} - STOP when done");
    }

    public bool Mime { get; private set; }

    /// <summary>Stops recording and reviews the clip.</summary>
    public void Stop()
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

        if (_section is { } section) _section = (section.From, Math.Min(section.To, _recordedAt + NewClip.Duration));
        Review();
    }

    /// <summary>The actor performs the clip at you, with the rest of the chart.</summary>
    public void Review()
    {
        if (NewClip == null || Chart == null || Entry == null) return;

        if (_section is { } section)
        {
            IReadOnlyList<TrackClip> before = Chart.Tracks.FirstOrDefault(t => t.ActorId == _actorId)?.Clips ?? Array.Empty<TrackClip>();
            var spliceTracks = Chart.Tracks.Where(t => t.ActorId != _actorId).ToList();
            spliceTracks.Add(new Track(_actorId, TrackEdits.PunchIn(before, NewClip.Name, section.From, section.To, Mime)));
            Chart spliced = Chart with { Tracks = spliceTracks };
            Mod.Battle.Start(spliced with { End = Math.Min(spliced.End, section.To + PrerollSeconds) }, _arenaOverride, Entry.AssetFolder,
                scoreKey: null, BattleMode.Review, _actorId, pendingClip: NewClip, startAt: Math.Max(0, section.From - PrerollSeconds));
        }
        else
        {
            var tracks = Chart.Tracks.Where(t => t.ActorId != _actorId).ToList();
            tracks.Add(new Track(_actorId, new[] { new TrackClip(_recordedAt, NewClip.Name, 0) { Mime = Mime } }));
            Chart chart = Chart with { Tracks = tracks, End = Math.Max(Chart.End, _recordedAt + NewClip.Duration + TrackEdits.EndPadding) };
            Mod.Battle.Start(chart, _arenaOverride, Entry.AssetFolder, scoreKey: null, BattleMode.Review, _actorId, _lane, NewClip);
        }

        State = StudioState.Reviewing;
    }

    /// <summary>Saves the clip into the chart; returns what happened.</summary>
    public string Keep()
    {
        if (NewClip == null || Chart == null || Entry == null) return "no clip to keep";

        Clip clip = NewClip;
        Directory.CreateDirectory(ClipsFolder());
        File.WriteAllText(Path.Combine(ClipsFolder(), $"{clip.Name}.json"), ClipFile.Write(clip));

        var chartText = File.ReadAllText(Entry.File);
        var kept = _section is { } section
            ? TrackEdits.KeepSection(chartText, Chart, _actorId, clip, section.From, section.To, Mime)
            : TrackEdits.KeepWhole(chartText, Chart, _actorId, clip, _recordedAt, Mime);

        // The actor takes the recorder's body size, so the poses fit it on anyone's machine.
        if (PlayerManager.instance?.localPlayer?.Data?.PlayerMeasurement is { Length: > 0f } yours)
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

    /// <summary>Throws the clip away; back to choosing, for a section.</summary>
    public void Discard()
    {
        Mod.Battle.Stop("clip discarded");
        NewClip = null;
        if (_section != null) State = StudioState.ChoosingSection;
        else Off();
    }

    public void Again()
    {
        if (Entry == null) return;
        if (_section != null) RecordSection(Mime);
        else Record(Entry, _arenaOverride, Mime);
    }

    /// <summary>Starts choosing a section of the first actor's track, the middle third to begin with.</summary>
    public void ChooseSection(ChartEntry entry, ArenaKind? arenaOverride)
    {
        if (!HasTrack(entry) || !Begin(entry, arenaOverride)) return;

        Mod.Battle.Stop("choosing a section");

        var end = Chart!.End;
        Section = (Snap(end / 3), Snap(end * 2 / 3));
        if (Section.To <= Section.From) Section = (Section.From, Section.From + Step);
        State = StudioState.ChoosingSection;
    }

    /// <summary>Moves the section's start or end by a beat, or half a second without a tempo.</summary>
    public void Nudge(bool start, int steps)
    {
        if (Chart == null) return;

        double from = Section.From, to = Section.To;
        if (start) from = Math.Clamp(from + (steps * Step), 0, to - Step);
        else to = Math.Clamp(to + (steps * Step), from + Step, Chart.End + 30);
        Section = (Snap(from), Snap(to));
    }

    /// <summary>Plays the section as it is, from a little before it.</summary>
    public void PreviewSection()
    {
        if (Chart == null || Entry == null) return;

        Mod.Battle.Start(Chart with { End = Section.To + 1 }, _arenaOverride, Entry.AssetFolder, scoreKey: null, BattleMode.Review, _actorId,
            startAt: Math.Max(0, Section.From - PrerollSeconds));
        State = StudioState.PreviewingSection;
    }

    /// <summary>Records the section: from a little before it, standing where the actor stood as it starts.</summary>
    public void RecordSection(bool mime = false)
    {
        if (Chart == null || Entry == null) return;

        _section = Section;
        NewClip = null;
        Mime = mime;
        Mod.Battle.Start(Chart, _arenaOverride, Entry.AssetFolder, scoreKey: null, BattleMode.Record, _actorId,
            startAt: Math.Max(0, Section.From - PrerollSeconds), recordSpot: SpotAt(Section.From), mime: mime);
        State = Mod.Battle.State == BattleState.Idle ? StudioState.ChoosingSection : StudioState.Recording;
        Logger.Info($"[Studio] recording {ActorName} from {Section.From:0.0}s to {Section.To:0.0}s - it stops by itself");
    }

    public void Back()
    {
        Mod.Battle.Stop("section closed");
        Off();
    }

    /// <summary>Puts back the chart as it was before its last save; returns what happened.</summary>
    public static string Undo(ChartEntry entry)
    {
        var backup = entry.File + BackupExtension;
        if (!File.Exists(backup)) return $"{entry.Name} has nothing to undo";

        File.Copy(backup, entry.File, overwrite: true);
        File.Delete(backup);
        return $"undid the last save to {entry.Name}";
    }

    /// <summary>Records, keeps the setup preview current, and notices when a battle it started has ended.</summary>
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

    /// <summary>For a scene change: the preview went with the scene and is rebuilt.</summary>
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
            // Stopped elsewhere: a scene change, the battle keys.
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
            if (PlayerManager.instance?.localPlayer?.Controller is not { } you || Mod.Battle.Lane is not { } lane) return;
            _lane = lane;
            _recorder.Start(you, lane);
            _recordedAt = now;
            if (_section != null) Mod.Battle.Banner("<b>GO</b>", 0.8f);
        }

        _recorder.Update(now);
        var sectionOver = _section is { } section && now >= section.To;
        if (sectionOver || Mod.Battle.SongOver || _recorder.Length > MaxSeconds) Stop();
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

        PlayClip? playing = new ChartRunner(Chart, time).Schedule.OfType<PlayClip>().FirstOrDefault(t => t.ActorId == _actorId && t.Time <= time);
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

    private static ActorRig? You() =>
        PlayerManager.instance?.localPlayer?.Controller is PlayerController you ? ActorRig.From(you) : null;

    private string ClipsFolder() => Path.Combine(Entry!.AssetFolder, ChartPackage.ClipsFolder);
}
