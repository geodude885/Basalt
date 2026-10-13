using BossBattles.Actors;
using BossBattles.Services.Charts;
using BossBattles.Services.Clips;
using BossBattles.Services.Scoring;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;
using MelonLoader;
using UnityEngine;

namespace BossBattles.Battle;

public enum BattleState
{
    Idle,
    LoadingArena,
    SpawningActors,
    Countdown,
    Running,
    Finished,
}

public enum BattleMode
{
    /// <summary>A battle: survive it.</summary>
    Play,

    /// <summary>Recording a clip in the actor's place: no actors, no projectiles, no end.</summary>
    Record,

    /// <summary>Watching a clip just recorded, as a player would face it; not scored.</summary>
    Review,
}

/// <summary>
/// Runs a battle: loads the arena, lays out the lane, spawns the chart's actors, counts down, then
/// executes each <see cref="ChartCommand"/> the <see cref="ChartRunner"/> hands back. You win by
/// lasting to the end; your health reaching 0 loses.
/// </summary>
internal sealed partial class BattleController
{
    private const float ArenaSettleSeconds = 1.5f;
    private const float ActorSpawnTimeoutSeconds = 15f;
    private const float PhaseBannerSeconds = 2f;
    private const float PhaseColourFadeSeconds = 1f;

    /// <summary>A long MP3 can take a while to decode; the countdown waits this long for it.</summary>
    private const float MusicWaitSeconds = 90f;

    private readonly LaneVisuals _lane = new();
    private readonly Projectiles _projectiles = new();
    private readonly Board _board = new();
    private readonly List<Actor> _actors = [];
    private readonly Music _music = new();
    private ArenaSpec _arenaSpec = ArenaSpec.Here;
    private ClipLibrary? _chartClips;
    private string? _scoreKey;
    private float _musicDeadline;
    private LaneFrame? _fixedFrame;
    private string? _recordActor;
    private Clip? _pendingClip;
    private System.Numerics.Vector2? _recordSpot;
    private LaneFrame _frame;
    private ChartRunner? _runner;
    private ScoreKeeper? _score;
    private Phase? _phase;
    private float _startedAt;
    private float _countdownEnds;
    private float _spawnDeadline;
    private int _session;

    public event Action<BattleResult>? Finished;

    public BattleState State { get; private set; } = BattleState.Idle;

    public BattleMode Mode { get; private set; } = BattleMode.Play;

    public Chart? Chart { get; private set; }

    public ArenaKind CurrentArena { get; private set; }

    public BattleResult? LastResult { get; private set; }

    public bool LastWasBest { get; private set; }

    public float CountdownSeconds { get; set; } = 3f;

    public bool ShowBackstop { get; set; } = true;

    public bool ShowPhaseBanners { get; set; } = true;

    /// <summary>Recording a mimed clip: the chart's projectiles fly and your moves stay off.</summary>
    public bool Miming { get; private set; }

    /// <summary>While true the match handler may not send you back to the Gym (see ArenaPatches).</summary>
    public bool HoldsArena => State != BattleState.Idle && Arena.IsMatchMap(CurrentArena);

    /// <summary>Your music volume, 0 to 1; a chart's own volume applies on top.</summary>
    public float MusicVolume
    {
        get => _music.Volume;
        set => _music.Volume = value;
    }

    public bool SongOver => Chart?.Music != null && _music.Ended;

    public ScoreBook? Scores { get; set; }

    /// <summary>Seconds into the chart: the song's clock when there is one, else game time; -1 before GO.</summary>
    public double ChartTime => State is BattleState.Running or BattleState.Finished ? StartAt + (_music.Clock ?? (Time.time - _startedAt)) : -1;

    /// <summary>Chart time the battle started from: 0, or part-way for a section.</summary>
    public double StartAt { get; private set; }

    public LaneFrame? Lane => LaidOut ? _frame : null;

    public IReadOnlyList<Actor> Actors => _actors;

    private bool LaidOut => State is not (BattleState.Idle or BattleState.LoadingArena) && Chart != null;

    /// <summary>A world point in lane coordinates: x across, z along, in chart units.</summary>
    public Vector3 ToLane(Vector3 world) => StringHelpers.ToUnity(_frame.ToLane(StringHelpers.ToNumerics(world)));

    public void Banner(string text, float seconds) => _board.Show(text, seconds);

    /// <param name="chart">The chart.</param>
    /// <param name="arenaOverride">An arena to play in instead of the chart's own.</param>
    /// <param name="assetFolder">Where its song and <c>clips/</c> are.</param>
    /// <param name="scoreKey">Files the result in <see cref="Scores"/>; null keeps no score.</param>
    /// <param name="mode">Play, record a clip as <paramref name="recordActor"/>, or review one.</param>
    /// <param name="recordActor">The actor recorded; default the first.</param>
    /// <param name="lane">A lane already laid out (a clip's) instead of laying one now.</param>
    /// <param name="pendingClip">A clip not yet saved, found by name before the chart's clips.</param>
    /// <param name="startAt">Chart time to start from, for a section.</param>
    /// <param name="recordSpot">Recording: where in the lane to stand instead of the actor's spot.</param>
    /// <param name="mime">Recording a mimed clip.</param>
    public void Start(
        Chart chart,
        ArenaKind? arenaOverride = null,
        string? assetFolder = null,
        string? scoreKey = null,
        BattleMode mode = BattleMode.Play,
        string? recordActor = null,
        LaneFrame? lane = null,
        Clip? pendingClip = null,
        double startAt = 0,
        System.Numerics.Vector2? recordSpot = null,
        bool mime = false)
    {
        Miming = mode == BattleMode.Record && mime;
        ArenaKind arena = arenaOverride ?? chart.Arena.Map;
        if (State != BattleState.Idle) Stop("a new battle was started");
        if (PlayerManager.instance?.localPlayer?.Controller == null)
        {
            Logger.Warning("[Battle.NotStarted] no local player yet");
            return;
        }

        Chart = chart;
        CurrentArena = arena;
        Mode = mode;
        _recordActor = recordActor ?? chart.DefaultActor.Id;
        _fixedFrame = lane;
        _pendingClip = pendingClip;
        StartAt = Math.Max(0, startAt);
        _recordSpot = recordSpot;
        // An overridden arena keeps the chart's spot only on the chart's own map.
        _arenaSpec = arena == chart.Arena.Map ? chart.Arena : new ArenaSpec(arena);
        _scoreKey = scoreKey;
        _session++;
        LastResult = null;
        LastWasBest = false;

        _chartClips = null;
        var clipFolder = assetFolder == null ? null : Path.Combine(assetFolder, ChartPackage.ClipsFolder);
        if (clipFolder != null && Directory.Exists(clipFolder))
        {
            _chartClips = new ClipLibrary(clipFolder);
            _chartClips.Reload();
            foreach (var error in _chartClips.Errors) Logger.Warning($"[Battle.BadClip] {error}");
        }

        _music.Clear();
        if (chart.Music != null)
        {
            var song = assetFolder == null ? null : ChartPaths.Inside(assetFolder, chart.Music.File);
            if (song == null) Logger.Warning($"[Music.Missing] {chart.Name}: no folder to find {chart.Music.File} in - playing without music");
            else _music.Load(song);
        }

        Logger.Info($"[Battle] {chart.Name} in {(arena == ArenaKind.Here ? "place" : $"the {arena}")}{(chart.Music != null ? $" to {chart.Music.File}" : "")}: {chart.Actors.Count} actor(s), {chart.Events.Count} events, {chart.End:0.#}s");

        // LoadingArena even when already there: actors are only placed while a battle is being set up.
        State = BattleState.LoadingArena;
        if (Arena.InScene(arena)) Prepare();
        else Arena.Load(arena);
    }

    public void Stop(string reason)
    {
        if (State == BattleState.Idle) return;

        Logger.Info($"[Battle.Stopped] {reason}");
        RestoreInvincibility();
        if (State == BattleState.Running && Mode != BattleMode.Record) FullHealth();
        EndVisuals();
        foreach (Actor actor in _actors) actor.Despawn();
        _actors.Clear();
        _projectiles.Clear();
        _lane.Destroy();
        _board.Hide();
        _music.Clear();
        ReleaseYourMoves();
        GiveSpawnsBack();
        _runner = null;
        State = BattleState.Idle;
    }

    public void ReturnToGym()
    {
        Stop("returning to the Gym");
        Arena.LoadGym();
    }

    public void OnSceneLoaded(string scene)
    {
        // Everything built in the old scene went with it, your player too.
        EndVisuals();
        _movesHeldFor = null;
        _withheldFrom = null;
        _withheld.Clear();
        MoveEffects.Forget();
        _actors.Clear();
        _projectiles.Forget();
        _lane.Forget();
        _board.Forget();

        if (State == BattleState.LoadingArena && string.Equals(scene, Arena.SceneFor(CurrentArena), StringComparison.Ordinal))
        {
            MelonCoroutines.Start(PrepareAfterSettling(_session));
            return;
        }

        if (State != BattleState.Idle)
        {
            Logger.Info($"[Battle.Abandoned] left for {scene} mid-battle");
            _runner = null;
            State = BattleState.Idle;
        }
    }

    public void Update()
    {
        _board.Update();

        switch (State)
        {
            case BattleState.SpawningActors:
                if (_actors.All(a => a.Ready) || Time.time >= _spawnDeadline) BeginCountdown();
                break;

            case BattleState.Countdown:
                KeepLane();
                var left = _countdownEnds - Time.time;
                if (left > 0f) _board.Show($"<b>{Chart!.Name}</b>\n{Mathf.CeilToInt(left)}");
                else if (_music.State == MusicState.Loading && Time.time < _musicDeadline) _board.Show($"<b>{Chart!.Name}</b>\nloading music {_music.Progress * 100:0}%");
                else BeginRunning();
                break;

            case BattleState.Running:
                KeepLane();
                _projectiles.Update();
                KeepYouVulnerable();
                UpdateVisuals();
                CleanUpStillStructures();
                foreach (ChartCommand command in _runner!.Advance(Math.Max(0, ChartTime)))
                {
                    Execute(command);
                    if (State != BattleState.Running) break;
                }

                break;

            case BattleState.Finished:
                // The last launches still get their flight check.
                KeepLane();
                _projectiles.Update();
                break;
        }
    }

    public void OnPlayerDamaged(int amount, int healthNow)
    {
        if (State != BattleState.Running || Mode == BattleMode.Record) return;

        _score!.PlayerDamaged(amount);
        Logger.Debug($"[Battle.YouWereHit] -{amount}, {healthNow} left");
        _reshade.Fire("hit", YourHead(), Mathf.Clamp01(amount / 5f), 0.6f);
        if (healthNow <= 0) Finish(survived: false, ChartTime);
    }

    public void OnActorDamaged(Actor actor, int amount)
    {
        if (State != BattleState.Running || !_actors.Contains(actor)) return;
        _score!.ActorDamaged(amount, _phase?.ScoreMultiplier ?? 1);
    }

    private void KeepLane()
    {
        _lane.Tick();
        _lane.LetStructuresThrough();
        KeepYouInLane();
    }

    private void BeginCountdown()
    {
        var ready = _actors.Count(a => a.Ready);
        if (ready < _actors.Count) Logger.Warning($"[Battle.ActorsLate] {_actors.Count - ready} of {_actors.Count} actor(s) not ready after {ActorSpawnTimeoutSeconds}s - starting anyway");

        _countdownEnds = Time.time + CountdownSeconds;
        _musicDeadline = _countdownEnds + MusicWaitSeconds;
        _lane.LetActorsThrough(_actors.SelectMany(a => a.Colliders));
        HoldYourMoves();
        ReShade.Refresh();
        _reshade.Preload(Chart!.Visuals.Select(v => v.Pack));
        State = BattleState.Countdown;
    }

    private void BeginRunning()
    {
        // Recording runs only the song and phases, with no end; miming keeps the projectiles too.
        _runner = new ChartRunner(Mode == BattleMode.Record
            ? Chart! with
            {
                Events = Miming ? Chart.Events.OfType<ProjectileEvent>().ToArray() : Array.Empty<ChartEvent>(),
                Tracks = Array.Empty<Track>(),
                End = double.MaxValue,
            }
            : Chart!, StartAt);
        _score = new ScoreKeeper(Chart!.Name);
        _phase = Chart.Phases[0];
        _startedAt = Time.time;
        if (_music.State == MusicState.Loading) Logger.Warning($"[Music.Late] still loading after {MusicWaitSeconds}s - playing without music");
        if (Chart.Music != null) _music.Play(Chart.Music.Start + StartAt, Chart.Music.Volume);
        _board.Show("<b>GO</b>", 1f);
        if (Mode != BattleMode.Record) FullHealth();
        foreach (Actor actor in _actors) actor.FullHeal();
        _still.Clear();
        KeepYouVulnerable();
        ReShade.Refresh();
        _lastBeat = -1;
        if (Chart.Visuals.Count > 0) Logger.Debug($"[Visuals.Chart] {Chart.Visuals.Count} look(s); installed: {ReShade.Describe()}");
        ReleaseYourMoves();
        State = BattleState.Running;
    }

    private void Execute(ChartCommand command)
    {
        try
        {
            switch (command)
            {
                case EnterPhase enter:
                    _phase = enter.Phase;
                    if (enter.Time > 0 && ShowPhaseBanners) _board.Show($"<b>{enter.Phase.Name}</b>{(enter.Phase.ScoreMultiplier != 1 ? $"\nx{enter.Phase.ScoreMultiplier:0.#} damage" : "")}", PhaseBannerSeconds);
                    Logger.Debug($"[Battle.Phase] {enter.Phase.Name} at {enter.Time:0.0}s");
                    if (enter.Time > 0) _reshade.Fire("phase", World(0f, 0f, 0f), 1f, 1.5f);
                    _lane.Recolour(Chart!.ColoursIn(enter.Phase), enter.Time > 0 && enter.Time > StartAt ? PhaseColourFadeSeconds : 0f);
                    break;

                case PlaceActor place:
                    if (Mode == BattleMode.Record) break;

                    // A clip holds its actor to the recorded path, so a move elsewhere would be dragged back.
                    Actor? placed = ActorById(place.ActorId);
                    if (placed is { PlayingClip: true }) Logger.Info($"[Battle.PlaceDuringClip] {place.ActorId} is playing a clip at {place.Time:0.0}s - its \"at\" is skipped");
                    else placed?.Place(World(place.At.X, 0f, place.At.Z), _frame.ActorYawDegrees(place.At.Facing));
                    break;

                case SpawnProjectile spawn:
                    _projectiles.Spawn(spawn.Id, spawn.Projectile, _frame);
                    _reshade.Fire("spawn", World(spawn.Projectile.From.X, spawn.Projectile.From.Y, spawn.Projectile.From.Z), 1f, (float)Math.Max(0.2, spawn.Projectile.Telegraph));
                    break;

                case LaunchProjectile launch:
                    _projectiles.Launch(launch.Id, launch.Projectile, AimPoint(launch.Projectile.To));
                    _reshade.Fire("launch", World(launch.Projectile.From.X, launch.Projectile.From.Y, launch.Projectile.From.Z), 1f, 0.5f);
                    _score!.ProjectileLaunched();
                    break;

                case RunMove move:
                    ActorById(move.ActorId)?.RunMove(move.Stack);
                    break;

                case PlayClip play:
                    Clip? clip = FindClip(play.Clip);
                    if (clip == null) Logger.Warning($"[Battle.NoClip] no clip called \"{play.Clip}\" in the chart's clips");
                    else ActorById(play.ActorId)?.PlayClip(clip, play.From, play.To, _frame, () => ChartTime, play.Mime);
                    break;

                case EndChart end:
                    Finish(survived: true, end.Time);
                    break;
            }
        }
        catch (Exception e)
        {
            Logger.Error($"[Battle.CommandFailed] {command}: {e}");
        }
    }

    private Vector3 AimPoint(Aim aim)
    {
        // Miming, you stand at the boss's end: "the player" is the lane's near end.
        if (aim.AtPlayer && Miming) return World(0f, 1.3f, 0f);
        if (aim.AtPlayer)
        {
            PlayerController? you = PlayerManager.instance?.localPlayer?.Controller;
            if (you != null) return ActorRig.From(you).Head.position + (Vector3.down * 0.25f);
        }

        return World(aim.X, aim.Y, 0f);
    }

    private void Finish(bool survived, double seconds)
    {
        if (State != BattleState.Running || _score == null) return;

        BattleResult result = _score.Finish(survived, seconds, Chart!.End);
        LastResult = result;
        State = BattleState.Finished;
        _music.FadeOut();
        GiveSpawnsBack();
        RestoreInvincibility();
        if (Mode != BattleMode.Record) FullHealth();
        _reshade.Fire(survived ? "survived" : "defeated", YourHead(), 1f, 2f);
        EndVisuals();

        BestScore? previous = _scoreKey == null ? null : Scores?.Best(_scoreKey);
        try
        {
            LastWasBest = _scoreKey != null && Scores != null && Scores.Record(_scoreKey, result, DateTime.Now);
        }
        catch (IOException e)
        {
            Logger.Error($"[Scores.SaveFailed] {e.Message}");
        }

        _board.ShowResult(result, LastWasBest ? "NEW BEST" : previous == null ? null : $"best {previous.Rank} {previous.Score}");
        Logger.Info(
            $"[Battle] {result.Chart}: {(result.Survived ? "survived" : "defeated")} - rank {result.Rank}, score {result.Score}; " +
            $"damage taken {result.DamageTaken} in {result.HitsTaken} hits, boss damage {result.ActorDamage:0.#}, " +
            $"{result.ProjectilesLaunched} projectiles in {result.Seconds:0.0}s");

        foreach (Actor actor in _actors) actor.Unplant();
        Finished?.Invoke(result);
    }

    /// <summary>A pending clip, then the chart's own clips.</summary>
    private Clip? FindClip(string name) =>
        _pendingClip != null && string.Equals(_pendingClip.Name, name, StringComparison.OrdinalIgnoreCase)
            ? _pendingClip
            : _chartClips?.Find(name);

    private Actor? ActorById(string id)
    {
        Actor? actor = _actors.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));
        if (actor == null || !actor.Exists) Logger.Warning($"[Battle.NoActor] \"{id}\" is not in play");
        return actor is { Exists: true } ? actor : null;
    }

    private Vector3 World(float x, float y, float z) => StringHelpers.ToUnity(_frame.ToWorld(new System.Numerics.Vector3(x, y, z)));
}
