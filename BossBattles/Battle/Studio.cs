using BossBattles.Actors;
using BossBattles.Core;
using BossBattles.Services.Charts;
using BossBattles.Services.Clips;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;
using UnityEngine;

namespace BossBattles.Battle;

/// <summary>Where the studio is.</summary>
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

    /// <summary>You stand in the actor's place; the take (or section) is being recorded.</summary>
    Recording,

    /// <summary>The actor performs the take at you, as a player will see it.</summary>
    Reviewing,

    /// <summary>The take waits for keep, again, replay or discard.</summary>
    Deciding,
}

/// <summary>
/// Chart editing in the game: setting the lane up, recording an actor's performance, and
/// recording a section of it again. Recording puts you in the actor's place, facing back down the
/// lane; from GO the <see cref="TakeRecorder"/> takes your poses, path and moves on the chart's clock
/// (the song, if any) until <see cref="Stop"/>, the song's end or <see cref="MaxSeconds"/> - or, for
/// a section, its end. You are then put back at your own end and the actor performs the take at
/// you; <see cref="Keep"/> saves it into the chart's <c>clips/</c> and <c>tracks</c> (a section is
/// spliced in: <see cref="Takes.PunchIn"/>). Every save keeps the chart as it was in
/// <c>chart.json.bak</c>, which <see cref="Undo"/> puts back. Only the edited values of the chart's
/// text change; the rest - comments included - stays as written.
/// </summary>
internal sealed class Studio
{
    /// <summary>A recording stops by itself after this long.</summary>
    public const double MaxSeconds = 600;

    /// <summary>A section is recorded, previewed and reviewed from this long before it - time to get into it.</summary>
    public const double PrerollSeconds = 2;

    private const string BackupExtension = ".bak";

    private readonly TakeRecorder _recorder = new();
    private readonly LaneVisuals _preview = new();
    private readonly List<GameObject> _markers = [];
    private ArenaKind? _arenaOverride;
    private LaneFrame? _lane;
    private string _actorId = "";

    // A section being recorded again; null while recording whole.
    private (double From, double To)? _section;

    // Lane setup's working values.
    private ArenaSpec _setupArena = ArenaSpec.Here;
    private Lane _setupLane;
    private List<ActorSpec> _setupActors = [];
    private (Vector3 Floor, Vector3 Facing)? _setupFrom;
    private LaneFrame? _setupFrame;
    private bool _setupDirty;

    public StudioState State { get; private set; } = StudioState.Off;

    /// <summary>The take waiting for a decision, if any.</summary>
    public Clip? Take { get; private set; }

    /// <summary>The chart being edited.</summary>
    public ChartEntry? Entry { get; private set; }

    /// <summary>The section chosen (or being recorded).</summary>
    public (double From, double To) Section { get; private set; }

    /// <summary>Whether the take being decided on is a section.</summary>
    public bool IsSection => _section != null;

    public string ActorName => Chart?.Actors.FirstOrDefault(a => a.Id == _actorId)?.Name ?? _actorId;

    /// <summary>Seconds recorded and moves made so far - for the panel.</summary>
    public (double Seconds, int Moves) Progress => (_recorder.Length, _recorder.MoveCount);

    /// <summary>Lane setup's values - for the panel.</summary>
    public (ArenaSpec Arena, Lane Lane, ActorSpec? Actor) SetupValues => (_setupArena, _setupLane, _setupActors.FirstOrDefault());

    /// <summary>The chart being edited, as last loaded.</summary>
    public Chart? Chart { get; private set; }

    /// <summary>Whether a chart can be edited: it loads, and it is a file or folder (a package is read-only).</summary>
    public static bool CanEdit(ChartEntry? entry) => entry?.Result.Chart != null && entry.Source != ChartSource.Package;

    /// <summary>Whether a chart has a recorded track to record sections of.</summary>
    public static bool HasTrack(ChartEntry? entry) =>
        CanEdit(entry) && entry!.Result.Chart!.Tracks.Any(t => t.ActorId == entry.Result.Chart.DefaultActor.Id && t.Takes.Count > 0);

    /// <summary>Whether a chart has a save to undo.</summary>
    public static bool CanUndo(ChartEntry? entry) => CanEdit(entry) && File.Exists(entry!.File + BackupExtension);

    // ---- Recording a whole take -----------------------------------------------------------------

    /// <summary>
    /// Starts recording the first actor of <paramref name="entry"/>'s chart. Mimed: the chart's
    /// projectiles fly as you perform and nothing you do spawns - kept, the actor moves as you did
    /// but runs no moves.
    /// </summary>
    public void Record(ChartEntry entry, ArenaKind? arenaOverride, bool mime = false)
    {
        if (!Begin(entry, arenaOverride)) return;

        _section = null;
        Mime = mime;
        Mod.Battle.Start(Chart!, arenaOverride, entry.AssetFolder, scoreKey: null, BattleMode.Record, _actorId, mime: mime);
        State = Mod.Battle.State == BattleState.Idle ? StudioState.Off : StudioState.Recording;
        Mod.Say($"[Studio] {(mime ? "miming" : "recording")} {ActorName} for {Chart!.Name} - STOP (or Ctrl+B) when done");
    }

    /// <summary>Whether the take being made is mimed.</summary>
    public bool Mime { get; private set; }

    /// <summary>Stops recording and reviews the take.</summary>
    public void Stop()
    {
        if (State != StudioState.Recording) return;

        var name = Takes.NextName(ClipsFolder(), _actorId);
        Take = _recorder.Stop(name);
        _lane = Mod.Battle.Lane ?? _lane;
        Mod.Battle.Stop("recording stopped");

        if (Take == null)
        {
            State = _section != null ? StudioState.ChoosingSection : StudioState.Off;
            Mod.Say("[Studio] nothing was recorded");
            return;
        }

        if (_section is { } section) _section = (section.From, Math.Min(section.To, Take.Duration));
        Review();
    }

    /// <summary>The actor performs the take at you, with the rest of the chart.</summary>
    public void Review()
    {
        if (Take == null || Chart == null || Entry == null) return;

        if (_section is { } section)
        {
            // From a little before the section to a little after it, with the section spliced in.
            Chart spliced = Spliced(Take, section.From, section.To);
            Mod.Battle.Start(spliced with { End = Math.Min(spliced.End, section.To + PrerollSeconds) }, _arenaOverride, Entry.AssetFolder,
                scoreKey: null, BattleMode.Review, _actorId, pendingTake: Take, startAt: Math.Max(0, section.From - PrerollSeconds));
        }
        else
        {
            var tracks = Chart.Tracks.Where(t => t.ActorId != _actorId).ToList();
            tracks.Add(new Track(_actorId, new[] { new TakeSegment(Take.Start, Take.Name, Take.Start) { Mime = Mime } }));
            Chart chart = Chart with { Tracks = tracks, End = Math.Max(Chart.End, Take.Duration + Takes.EndPadding) };
            Mod.Battle.Start(chart, _arenaOverride, Entry.AssetFolder, scoreKey: null, BattleMode.Review, _actorId, _lane, Take);
        }

        State = StudioState.Reviewing;
    }

    /// <summary>Saves the take into the chart; returns what happened, for the log.</summary>
    public string Keep()
    {
        if (Take == null || Chart == null || Entry == null) return "no take to keep";

        Clip take = Take;
        Directory.CreateDirectory(ClipsFolder());
        File.WriteAllText(Path.Combine(ClipsFolder(), $"{take.Name}.json"), ClipFile.Write(take));

        var chartText = File.ReadAllText(Entry.File);
        var kept = _section is { } section
            ? Takes.KeepSection(chartText, Chart, _actorId, take, section.From, section.To, Mime)
            : Takes.KeepWhole(chartText, Chart, _actorId, take, Mime);
        Save(chartText, kept);

        Mod.Battle.Stop("take kept");
        var what = _section is { } s
            ? $"kept {take.Name} over {s.From:0.0}-{s.To:0.0}s ({take.Moves.Count} moves) in {Entry.Name}"
            : $"kept {take.Name} ({take.Duration - take.Start:0.0}s, {take.Moves.Count} moves) in {Entry.Name}";
        Off();
        Mod.Note("Studio.Kept", what, echo: false);
        return what;
    }

    /// <summary>Throws the take away - back to choosing, for a section.</summary>
    public void Discard()
    {
        Mod.Battle.Stop("take discarded");
        Take = null;
        if (_section != null) State = StudioState.ChoosingSection;
        else Off();
    }

    /// <summary>Records the same take (or section) again.</summary>
    public void Again()
    {
        if (Entry == null) return;
        if (_section != null) RecordSection(Mime);
        else Record(Entry, _arenaOverride, Mime);
    }

    // ---- Recording a section again --------------------------------------------------------------

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

    /// <summary>Moves the section's start or end by a beat (or half a second).</summary>
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
        Take = null;
        Mime = mime;
        Mod.Battle.Start(Chart, _arenaOverride, Entry.AssetFolder, scoreKey: null, BattleMode.Record, _actorId,
            startAt: Math.Max(0, Section.From - PrerollSeconds), recordSpot: SpotAt(Section.From), mime: mime);
        State = Mod.Battle.State == BattleState.Idle ? StudioState.ChoosingSection : StudioState.Recording;
        Mod.Say($"[Studio] recording {ActorName} from {Section.From:0.0}s to {Section.To:0.0}s - it stops by itself");
    }

    /// <summary>Back from choosing a section.</summary>
    public void Back()
    {
        Mod.Battle.Stop("section closed");
        Off();
    }

    // ---- Lane setup -----------------------------------------------------------------------------

    /// <summary>Starts setting up <paramref name="entry"/>'s lane, in its own arena.</summary>
    public void BeginSetup(ChartEntry entry)
    {
        if (!Begin(entry, null)) return;

        // A finished battle's lane would otherwise stay up beside the preview.
        Mod.Battle.Stop("lane setup");

        _setupArena = Chart!.Arena;
        _setupLane = Chart.Lane;
        _setupActors = Chart.Actors.ToList();
        _setupFrom = null;
        _setupDirty = true;
        State = StudioState.Setup;

        if (!Arena.InScene(_setupArena.Map)) Arena.Load(_setupArena.Map);
    }

    public void Resize(float lengthChange, float widthChange)
    {
        (_setupLane, IReadOnlyList<ActorSpec> actors) = ChartSetup.Resize(_setupLane, _setupActors, _setupLane.Length + lengthChange, _setupLane.Width + widthChange);
        _setupActors = actors.ToList();
        _setupDirty = true;
    }

    /// <summary>Your spot, facing the way you face, becomes the lane's start - on whichever map you are.</summary>
    public void LaneHere()
    {
        if (You() is not { } rig) return;

        ArenaKind map = CurrentMap();
        _setupFrom = (rig.FloorPoint, rig.Facing);
        if (map == ArenaKind.Here)
        {
            _setupArena = ArenaSpec.Here;
        }
        else
        {
            Vector3 at = rig.FloorPoint - (Arena.Anchor(map) ?? Vector3.zero);
            _setupArena = new ArenaSpec(map, MathF.Round(at.x, 2), MathF.Round(at.z, 2), MathF.Round(Arena.Bearing(rig.Facing)));
        }

        _setupDirty = true;
    }

    /// <summary>The actor's spot becomes where you stand (the lane grows to reach it if needed).</summary>
    public void ActorHere()
    {
        if (You() is not { } rig || _setupFrame is not { } frame || _setupActors.Count == 0) return;

        System.Numerics.Vector3 at = frame.ToLane(Describe.ToNumerics(rig.FloorPoint));
        var z = Math.Max(0.5f, at.Z);
        if (z > _setupLane.Length) _setupLane = new Lane(Math.Min(ChartSetup.MaxLength, MathF.Ceiling(z)), _setupLane.Width);

        var x = Math.Clamp(at.X, -_setupLane.Width / 2f, _setupLane.Width / 2f);
        _setupActors[0] = _setupActors[0] with { At = new Spot(MathF.Round(x, 2), MathF.Round(Math.Min(z, _setupLane.Length), 2)) };
        _setupDirty = true;
    }

    /// <summary>The next map - here, the Gym, the Ring, the Pit - loading it when it is a different scene.</summary>
    public void NextMap()
    {
        var next = (ArenaKind)(((int)_setupArena.Map + 1) % Enum.GetValues<ArenaKind>().Length);
        _setupArena = new ArenaSpec(next);
        _setupFrom = null;
        _setupDirty = true;
        if (!Arena.InScene(next)) Arena.Load(next);
    }

    /// <summary>Writes the lane setup into the chart; returns what happened, for the log.</summary>
    public string SaveSetup()
    {
        if (Entry == null || State != StudioState.Setup) return "no lane setup to save";

        // A longer or shorter lane takes the chart with it: every distance along the lane is scaled,
        // so nothing is left past the far end (that broke a chart, which then would not load).
        var text = File.ReadAllText(Entry.File);
        var factor = Chart!.Lane.Length > 0 ? _setupLane.Length / Chart.Lane.Length : 1f;
        var written = ChartSetup.ScaleDepth(ChartSetup.Write(text, _setupArena, _setupLane, _setupActors), factor, _setupLane.Length);
        Save(text, written);
        var what = $"saved {Entry.Name}'s lane: {Summarise(_setupArena)}, {_setupLane.Length:0.#} x {_setupLane.Width:0.#} m";
        EndSetup();
        Mod.Note("Studio.SetupSaved", what, echo: false);
        return what;
    }

    public void CancelSetup() => EndSetup();

    // ---- Undo -----------------------------------------------------------------------------------

    /// <summary>Puts back the chart as it was before its last save; returns what happened.</summary>
    public static string Undo(ChartEntry entry)
    {
        var backup = entry.File + BackupExtension;
        if (!File.Exists(backup)) return $"{entry.Name} has nothing to undo";

        File.Copy(backup, entry.File, overwrite: true);
        File.Delete(backup);
        return $"undid the last save to {entry.Name}";
    }

    // ---- Per frame ------------------------------------------------------------------------------

    /// <summary>Each frame: records, keeps the setup preview current, and notices when a battle it started has ended.</summary>
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

    /// <summary>For a scene change: the preview went with the scene, and is rebuilt in the new one.</summary>
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
            // Stopped elsewhere - a scene change, the battle keys.
            _recorder.Cancel();
            Off();
            return;
        }

        if (Mod.Battle.State != BattleState.Running) return;
        var now = Mod.Battle.ChartTime;

        if (!_recorder.Recording)
        {
            var from = _section?.From ?? 0;
            if (now < from)
            {
                Mod.Battle.Banner($"<b>{Math.Ceiling(from - now):0}</b>", 0.2f);
                return;
            }

            if (PlayerManager.instance?.localPlayer?.Controller is not { } you || Mod.Battle.Lane is not { } lane) return;
            _lane = lane;
            _recorder.Start(you, lane, () => Mod.Battle.ChartTime);
            if (_section != null) Mod.Battle.Banner("<b>GO</b>", 0.8f);
        }

        _recorder.Update();
        var sectionOver = _section is { } section && now >= section.To;
        if (sectionOver || Mod.Battle.SongOver || _recorder.Length > MaxSeconds) Stop();
    }

    // ---- Helpers --------------------------------------------------------------------------------

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
        Take = null;
        return true;
    }

    private void Off()
    {
        State = StudioState.Off;
        Take = null;
        _section = null;
    }

    private void EndSetup()
    {
        _preview.Destroy();
        foreach (GameObject marker in _markers)
        {
            if (marker != null) UnityEngine.Object.Destroy(marker);
        }

        _markers.Clear();
        Off();
    }

    private void BuildPreview()
    {
        if (You() is not { } rig) return;
        if (_setupArena.Map != ArenaKind.Here && !Arena.InScene(_setupArena.Map)) return;

        _setupFrom ??= (rig.FloorPoint, rig.Facing);
        LaneFrame? frame = Arena.Frame(_setupArena.Map, _setupArena, _setupLane, _setupFrom.Value.Floor, _setupFrom.Value.Facing);
        if (frame == null) return;

        _setupFrame = frame;
        _setupDirty = false;
        _preview.Build(frame.Value, _setupLane, showBackstop: true, walls: false);

        foreach (GameObject marker in _markers)
        {
            if (marker != null) UnityEngine.Object.Destroy(marker);
        }

        _markers.Clear();
        foreach (ActorSpec actor in _setupActors)
        {
            Vector3 foot = Describe.ToUnity(frame.Value.ToWorld(new System.Numerics.Vector3(actor.At.X, 0f, actor.At.Z)));
            _markers.Add(Marker(foot));
        }
    }

    /// <summary>A post where an actor will stand.</summary>
    private static GameObject Marker(Vector3 foot)
    {
        var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        post.name = "BossBattles_ActorMarker";
        Collider collider = post.GetComponent<Collider>();
        if (collider != null) UnityEngine.Object.Destroy(collider);
        post.transform.position = foot + (Vector3.up * 0.9f);
        post.transform.localScale = new Vector3(0.35f, 0.9f, 0.35f);

        Material? material = Paint.Colour(new Color(0.25f, 0.5f, 0.95f));
        Renderer renderer = post.GetComponent<Renderer>();
        if (renderer != null && material != null) renderer.material = material;
        return post;
    }

    /// <summary>Where the actor stood at chart time <paramref name="time"/>, from the take playing then.</summary>
    private System.Numerics.Vector2? SpotAt(double time)
    {
        if (Chart == null || Entry == null) return null;

        PlayTake? playing = new ChartRunner(Chart, time).Schedule.OfType<PlayTake>().FirstOrDefault(t => t.ActorId == _actorId && t.Time <= time);
        if (playing == null) return null;

        var clips = new ClipLibrary(ClipsFolder());
        clips.Reload();
        return clips.Find(playing.Clip)?.Sample((float)playing.From).Lane;
    }

    private Chart Spliced(Clip take, double from, double to)
    {
        IReadOnlyList<TakeSegment> before = Chart!.Tracks.FirstOrDefault(t => t.ActorId == _actorId)?.Takes ?? Array.Empty<TakeSegment>();
        var tracks = Chart.Tracks.Where(t => t.ActorId != _actorId).ToList();
        tracks.Add(new Track(_actorId, Takes.PunchIn(before, take.Name, from, to, Mime)));
        return Chart with { Tracks = tracks };
    }

    private void Save(string before, string after)
    {
        File.WriteAllText(Entry!.File + BackupExtension, before);
        File.WriteAllText(Entry.File, after);
    }

    /// <summary>A beat at the section's start (half a second without a tempo).</summary>
    private double Step => Chart?.Tempo is { } tempo ? 60.0 / tempo.BpmAt(Section.From) : 0.5;

    /// <summary>To the nearest beat, through tempo changes; to the half second without a tempo.</summary>
    private double Snap(double seconds) => Chart?.Tempo is { } tempo
        ? Math.Round(tempo.Seconds(Math.Round(tempo.Beat(seconds))), 3)
        : Math.Round(Math.Round(seconds / 0.5) * 0.5, 3);

    private static ActorRig? You() =>
        PlayerManager.instance?.localPlayer?.Controller is PlayerController you ? ActorRig.Of(you) : null;

    private static ArenaKind CurrentMap()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        return new[] { ArenaKind.Gym, ArenaKind.Ring, ArenaKind.Pit }.FirstOrDefault(k => Arena.SceneFor(k) == scene);
    }

    private static string Summarise(ArenaSpec arena) =>
        arena.Map == ArenaKind.Here ? "here"
        : arena.HasSpot ? $"{arena.Map} at {arena.X:0.#}, {arena.Z:0.#} facing {arena.Facing:0}"
        : arena.Map.ToString();

    private string ClipsFolder() => Path.Combine(Entry!.AssetFolder, ChartPackage.ClipsFolder);
}
