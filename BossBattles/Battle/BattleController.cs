using System.Collections;
using BossBattles.Actors;
using BossBattles.Arenas;
using BossBattles.Presentation;
using BossBattles.Services;
using BossBattles.Services.Charts;
using BossBattles.Services.Clips;
using BossBattles.Services.Scoring;
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
    Play,
    Record,
    Review,
}

/// <summary>
/// Runs a battle: loads the arena, lays out the lane, spawns the chart's actors, counts down, then
/// executes each <see cref="ChartCommand"/> the <see cref="ChartRunner"/> hands back. You win by
/// lasting to the end; your health reaching 0 loses.
/// </summary>
internal class BattleController(ActorRegistry actors, JsonBook<BestScore> scores, GameAssets assets)
{
    private const float ArenaSettleSeconds = 1.5f;
    private const float ActorSpawnTimeoutSeconds = 15f;
    private const float PhaseBannerSeconds = 2f;
    private const float PhaseColourFadeSeconds = 1f;

    /// <summary>A newly ready actor placed at once had not settled and went on falling.</summary>
    private const float ActorSettleSeconds = 0.1f;

    /// <summary>A long MP3 can take a while to decode; the countdown waits this long for it.</summary>
    private const float MusicWaitSeconds = 90f;

    private readonly Arena _arena = new(assets);
    private readonly Projectiles _projectiles = new(actors);
    private readonly BattleInfoBoard _board = new(assets);
    private readonly MusicController _music = new();
    private readonly MoveLock _moves = new();
    private readonly StructureSweeper _sweeper = new();
    private readonly BattlePlayer _player = new();
    private ArenaSpec _arenaSpec = ArenaSpec.Here;
    private ClipLibrary? _chartClips;
    private string? _scoreKey;
    private float _musicDeadline;
    private Anchor? _fixedFrame;
    private string? _recordActor;
    private Clip? _pendingClip;
    private System.Numerics.Vector2? _recordSpot;
    private Anchor _frame;
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

    public bool Miming { get; private set; }

    public bool BetweenBattles => State is BattleState.Idle or BattleState.Finished;

    public bool StayInMap => State != BattleState.Idle && CurrentArena.IsMatchMap();

    public BattleVisualEvents Signals { get; } = new();

    public float MusicVolume
    {
        get => _music.Volume;
        set => _music.Volume = value;
    }

    public bool SongOver => Chart?.Music != null && _music.Ended;

    /// <summary>Seconds into the chart: the song's clock when there is one, else game time; -1 before GO.</summary>
    public double ChartTime => State is BattleState.Running or BattleState.Finished ? StartAt + (_music.Clock ?? (Time.time - _startedAt)) : -1;

    /// <summary>Chart time the battle started from: 0, or part-way for a section.</summary>
    public double StartAt { get; private set; }

    public Anchor? Lane => LaidOut ? _frame : null;

    private bool LaidOut => State is not (BattleState.Idle or BattleState.LoadingArena) && Chart != null;

    /// <summary>A world point in lane coordinates: x across, z along, in metres.</summary>
    public Vector3 ToLane(Vector3 world) => _frame.ToLocal(world.ToNumerics()).ToUnity();

    public void Banner(string text, float seconds) => _board.Show(text, seconds);

    /// <param name="chart">The chart.</param>
    /// <param name="arenaOverride">An arena to play in instead of the chart's own.</param>
    /// <param name="assetFolder">Where its song and <c>clips/</c> are.</param>
    /// <param name="scoreKey">Files the result in the score book; null keeps no score.</param>
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
        Anchor? lane = null,
        Clip? pendingClip = null,
        double startAt = 0,
        System.Numerics.Vector2? recordSpot = null,
        bool mime = false)
    {
        Miming = mode == BattleMode.Record && mime;
        var arena = arenaOverride ?? chart.Arena.Map;
        if (Maps.Online)
        {
            Logger.Info("[Battle.NotStarted] not in a park or match - charts play in the Gym");
            return;
        }

        if (State != BattleState.Idle) Stop("a new battle was started");
        if (BattlePlayer.Controller == null)
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
            var song = assetFolder == null ? null : ChartFiles.Inside(assetFolder, chart.Music.File);
            if (song == null) Logger.Warning($"[Music.Missing] {chart.Name}: no folder to find {chart.Music.File} in - playing without music");
            else _music.Load(song);
        }

        Logger.Info($"[Battle] {chart.Name} in {(arena == ArenaKind.Here ? "place" : $"the {arena}")}{(chart.Music != null ? $" to {chart.Music.File}" : "")}: {chart.Actors.Count} actor(s), {chart.Events.Count} events, {chart.End:0.#}s");

        // LoadingArena even when already there: actors are only placed while a battle is being set up.
        State = BattleState.LoadingArena;
        if (arena.IsLoaded()) Prepare();
        else arena.Load();
    }

    public void Stop(string reason)
    {
        if (State == BattleState.Idle) return;

        Logger.Info($"[Battle.Stopped] {reason}");
        _player.RestoreInvincibility();
        if (State == BattleState.Running && Mode != BattleMode.Record) BattlePlayer.FullHealth();
        Signals.End();
        actors.DespawnAll();
        _projectiles.Clear();
        _arena.Destroy();
        _board.Hide();
        _music.Clear();
        _moves.Release();
        _moves.GiveSpawnsBack();
        _runner = null;
        State = BattleState.Idle;
    }

    public void ReturnToGym()
    {
        Stop("returning to the Gym");
        Maps.LoadGym();
    }

    public void OnSceneLoaded(string scene)
    {
        // Everything built in the old scene went with it, your player too.
        Signals.End();
        _moves.Forget();
        _projectiles.Forget();
        _arena.Forget();
        _board.Forget();

        if (State == BattleState.LoadingArena && string.Equals(scene, CurrentArena.Scene(), StringComparison.Ordinal))
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
                if (actors.All.All(a => a.Ready) || Time.time >= _spawnDeadline) BeginCountdown();
                break;

            case BattleState.Countdown:
                KeepLane();
                var left = _countdownEnds - Time.time;
                if (left > 0f) _board.Show($"<b>{Chart!.Name}</b>\n{Mathf.CeilToInt(left)}");
                else if (_music.Loading && Time.time < _musicDeadline) _board.Show($"<b>{Chart!.Name}</b>\nloading music {_music.Progress * 100:0}%");
                else BeginRunning();
                break;

            case BattleState.Running:
                KeepLane();
                _projectiles.Update();
                if (Mode != BattleMode.Record) _player.KeepVulnerable();
                Signals.Update(Chart!, ChartTime);
                _sweeper.Update(Chart!.Rules.CleanupSeconds);
                foreach (var command in _runner!.Advance(Math.Max(0, ChartTime)))
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
        Signals.Fire("hit", BattlePlayer.Head, Mathf.Clamp01(amount / 5f), 0.6f);
        if (healthNow <= 0) Finish(survived: false, ChartTime);
    }

    public void OnActorDamaged(int amount)
    {
        if (State != BattleState.Running) return;
        _score!.ActorDamaged(amount, _phase?.ScoreMultiplier ?? 1);
    }

    private IEnumerator PrepareAfterSettling(int session)
    {
        // The match maps finish setting themselves up over the first frames.
        yield return new WaitForSeconds(ArenaSettleSeconds);
        if (session == _session && State == BattleState.LoadingArena) Prepare();
    }

    /// <summary>Lays the battle out: the lane, the board, you, and the actors on their spots.</summary>
    private void Prepare()
    {
        try
        {
            var yours = ActorRig.From(BattlePlayer.Controller!);
            var frame = _fixedFrame ?? _arenaSpec.CreateLaneAnchor(CurrentArena, yours.Feet, yours.Facing);
            if (frame == null)
            {
                Stop("no lane could be laid out");
                return;
            }

            _frame = frame.Value;
            StructureSweeper.ClearAll();
            var performer = Chart!.Actors.FirstOrDefault(a => a.Id == _recordActor) ?? Chart.DefaultActor;

            if (Mode == BattleMode.Record)
            {
                // In the actor's place, facing down the lane as it will; a section starts where it stood then.
                var spot = _recordSpot is { } at ? World(at.X, 0f, at.Y) : World(performer.At.X, 0f, performer.At.Z);
                _player.Place(spot, Quaternion.Euler(0f, _frame.ActorYaw(performer.At.Facing), 0f));
            }
            else if (CurrentArena.IsMatchMap() || _arenaSpec.HasSpot || _fixedFrame != null)
            {
                _player.Place(_frame.Position.ToUnity(), Quaternion.LookRotation(_frame.Forward.ToUnity()));
            }

            _arena.Build(_frame, Chart.Lane, ShowBackstop, walls: true, zone: Mode == BattleMode.Record ? null : Chart.Zone,
                colours: Chart.ColoursIn(Chart.Phases.LastOrDefault(p => p.Start <= StartAt)), panes: Chart.EverShowsWalls);
            if (Mode == BattleMode.Record)
            {
                _board.SetPosition(World(performer.At.X, 2.6f, Math.Max(0f, performer.At.Z - 3f)), World(performer.At.X, 0f, performer.At.Z));
                _board.Show($"<b>Recording {performer.Name}</b>\nget ready");
            }
            else
            {
                _board.SetPosition(World(0f, 2.6f, Math.Min(3f, Chart.Lane.Length / 2f)), _frame.Position.ToUnity());
                _board.Show($"<b>{Chart.Name}</b>\n{(Mode == BattleMode.Review ? "clip review" : "get ready")}");
            }

            foreach (var spec in Mode == BattleMode.Record ? Enumerable.Empty<ActorSpec>() : Chart.Actors)
            {
                var actor = new Actor(spec.Id, spec.Name, spec.Look, spec.Shiftstones) { SelfKnockbackMultiplier = 0f, Measurement = spec.Measurement };
                var spot = World(spec.At.X, 0f, spec.At.Z);
                var yaw = _frame.ActorYaw(spec.At.Facing);

                // Spawning puts the head on the floor, so the body falls until placed properly.
                var session = _session;
                actor.OnReady += ready => MelonCoroutines.Start(PlaceWhenSettled(ready, spot, yaw, session));
                actors.Spawn(actor, spot, Quaternion.Euler(0f, yaw, 0f));
            }

            _spawnDeadline = Time.time + ActorSpawnTimeoutSeconds;
            State = BattleState.SpawningActors;
        }
        catch (Exception e)
        {
            Logger.Error($"[Battle.PrepareFailed] {e}");
            Stop("preparing failed");
        }
    }

    private IEnumerator PlaceWhenSettled(Actor actor, Vector3 spot, float yaw, int session)
    {
        yield return new WaitForSecondsRealtime(ActorSettleSeconds);
        if (session == _session && actor.Exists && State is not (BattleState.Idle or BattleState.Running or BattleState.Finished)) actor.Place(spot, yaw);
    }

    private void KeepLane()
    {
        _arena.Visuals.Tick();
        _arena.Boundary.LetStructuresThrough();
        _player.KeepInLane(_frame, Chart!, zoned: Mode != BattleMode.Record);
    }

    private void BeginCountdown()
    {
        var ready = actors.All.Count(a => a.Ready);
        if (ready < actors.All.Count) Logger.Warning($"[Battle.ActorsLate] {actors.All.Count - ready} of {actors.All.Count} actor(s) not ready after {ActorSpawnTimeoutSeconds}s - starting anyway");

        _countdownEnds = Time.time + CountdownSeconds;
        _musicDeadline = _countdownEnds + MusicWaitSeconds;
        _arena.Boundary.LetActorsThrough(actors.All.SelectMany(a => a.Colliders));
        _moves.Hold();
        Signals.PreloadChartPacks(Chart!);
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
        if (_music.Loading) Logger.Warning($"[Music.Late] still loading after {MusicWaitSeconds}s - playing without music");
        if (Chart.Music != null) _music.Play(Chart.Music.Start + StartAt, Chart.Music.Volume);
        _board.Show("<b>GO</b>", 1f);
        foreach (var actor in actors.All) actor.FullHeal();
        _sweeper.Reset();
        Signals.Begin(Chart);

        if (Mode != BattleMode.Record)
        {
            BattlePlayer.FullHealth();
            _player.KeepVulnerable();
        }

        // Miming keeps your moves off: you throw the chart's shots by hand.
        if (!Miming) _moves.Release(Mode == BattleMode.Record ? SpawnRule.All : Chart.Rules.Spawn);
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
                    if (enter.Time > 0) Signals.Fire("phase", World(0f, 0f, 0f), 1f, 1.5f);
                    _arena.Visuals.Recolour(Chart!.ColoursIn(enter.Phase), enter.Time > 0 && enter.Time > StartAt ? PhaseColourFadeSeconds : 0f);
                    break;

                case PlaceActor place:
                    if (Mode == BattleMode.Record) break;

                    // A clip holds its actor to the recorded path, so a move elsewhere would be dragged back.
                    var placed = ActorById(place.ActorId);
                    if (placed is { PlayingClip: true }) Logger.Info($"[Battle.PlaceDuringClip] {place.ActorId} is playing a clip at {place.Time:0.0}s - its \"at\" is skipped");
                    else placed?.Place(World(place.At.X, 0f, place.At.Z), _frame.ActorYaw(place.At.Facing));
                    break;

                case SpawnProjectile spawn:
                    _projectiles.Spawn(spawn.Id, spawn.Projectile, _frame);
                    Signals.Fire("spawn", World(spawn.Projectile.From.X, spawn.Projectile.From.Y, spawn.Projectile.From.Z), 1f, (float)Math.Max(0.2, spawn.Projectile.Telegraph));
                    break;

                case LaunchProjectile launch:
                    _projectiles.Launch(launch.Id, launch.Projectile, AimPoint(launch.Projectile.To));
                    Signals.Fire("launch", World(launch.Projectile.From.X, launch.Projectile.From.Y, launch.Projectile.From.Z), 1f, 0.5f);
                    _score!.ProjectileLaunched();
                    break;

                case RunMove move:
                    ActorById(move.ActorId)?.RunMove(move.Stack);
                    break;

                case PlayClip play:
                    var clip = FindClip(play.Clip);
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
        return aim.AtPlayer && Miming
            ? World(0f, 1.3f, 0f)
            : aim.AtPlayer && BattlePlayer.Controller is { } you
            ? ActorRig.From(you).Head.position + (Vector3.down * 0.25f)
            : World(aim.X, aim.Y, 0f);
    }

    private void Finish(bool survived, double seconds)
    {
        if (State != BattleState.Running || _score == null) return;

        var result = _score.Finish(survived, seconds, Chart!.End);
        LastResult = result;
        State = BattleState.Finished;
        _music.FadeOut();
        _moves.GiveSpawnsBack();
        _player.RestoreInvincibility();
        if (Mode != BattleMode.Record) BattlePlayer.FullHealth();
        Signals.Fire(survived ? "survived" : "defeated", BattlePlayer.Head, 1f, 2f);
        Signals.End();

        var previous = _scoreKey == null ? null : scores.Get(_scoreKey);
        LastWasBest = _scoreKey != null && (previous == null || previous.IsBeatenBy(result));
        try
        {
            if (LastWasBest) scores.Set(_scoreKey!, BestScore.From(result, DateTime.Now));
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

        foreach (var actor in actors.All) actor.AllowFreePlacement();
        Finished?.Invoke(result);
    }

    /// <summary>A pending clip, then the chart's own clips.</summary>
    private Clip? FindClip(string name) =>
        _pendingClip != null && string.Equals(_pendingClip.Name, name, StringComparison.OrdinalIgnoreCase)
            ? _pendingClip
            : _chartClips?.Find(name);

    private Actor? ActorById(string id)
    {
        var actor = actors.All.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));
        if (actor == null || !actor.Exists) Logger.Warning($"[Battle.NoActor] \"{id}\" is not in play");
        return actor is { Exists: true } ? actor : null;
    }

    private Vector3 World(float x, float y, float z) => _frame.ToWorld(new System.Numerics.Vector3(x, y, z)).ToUnity();
}
