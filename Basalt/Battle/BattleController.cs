using System.Collections;
using Basalt.Actors;
using Basalt.Arenas;
using Basalt.Presentation;
using Basalt.Services;
using Basalt.Services.Charts;
using Basalt.Services.Clips;
using Basalt.Services.Scoring;
using Basalt.UI;
using MelonLoader;
using UnityEngine;

namespace Basalt.Battle;

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

internal class BattleController(ActorRegistry actors, JsonBook<BestScore> scores, GameAssets assets, Settings settings)
{
    private const float ArenaSettleSeconds = 1.5f;
    private const float ActorSpawnTimeoutSeconds = 15f;
    private const float PhaseBannerSeconds = 2f;
    private const float CountdownSeconds = 3f;
    private const float PhaseColourFadeSeconds = 1f;

    // A newly ready actor placed at once had not settled, and went on falling.
    private const float ActorSettleSeconds = 0.1f;

    // A long MP3 can take a while to decode; the countdown waits this long for it.
    private const float MusicWaitSeconds = 90f;

    private readonly Arena _arena = new(assets);
    private readonly Projectiles _projectiles = new(actors);
    private readonly BattleInfoBoard _board = new(assets);
    private readonly MusicController _music = new();
    private readonly MoveLock _moves = new();
    private readonly StructureSweeper _sweeper = new();
    private readonly BattlePlayer _player = new();

    public event Action<BattleResult>? Finished;

    public BattleRun? Run { get; private set; }
    public BattleVisualEvents Signals { get; } = new();

    public BattleState State => Run?.State ?? BattleState.Idle;

    public BattleMode Mode => Run?.Request.Mode ?? BattleMode.Play;

    public Chart? Chart => Run?.Chart;

    public bool BetweenBattles => State is BattleState.Idle or BattleState.Finished;

    public bool StayInMap => Run != null && Run.Request.Arena.IsMatchMap();

    public bool SongOver => Chart?.Music != null && _music.Ended;

    // Seconds into the chart: the song's clock when there is one, else game time; -1 before GO.
    public double ChartTime => Run is { State: BattleState.Running or BattleState.Finished } run
        ? _music.Clock ?? (Time.time - run.StartedAt)
        : -1;

    public Anchor? Lane => Run is { LaidOut: true } run ? run.Frame : null;
    public Vector3 ToLane(Vector3 world) => (Run?.Frame ?? default).ToLocal(world.ToNumerics()).ToUnity();

    public void Start(BattleRequest request)
    {
        if (Maps.IsCurrentlyOnline)
        {
            Logger.Info("[Battle.NotStarted] not in a park or match - charts play in the Gym");
            return;
        }

        if (State != BattleState.Idle) Stop("a new battle was started");
        if (BattlePlayer.Player?.Controller == null)
        {
            Logger.Warning("[Battle.NotStarted] no local player yet");
            return;
        }

        var chart = request.Chart;
        ClipLibrary? chartClips = null;
        var clipFolder = request.AssetFolder == null ? null : Path.Combine(request.AssetFolder, ChartPackage.ClipsFolder);
        if (clipFolder != null && Directory.Exists(clipFolder))
        {
            chartClips = new ClipLibrary(clipFolder);
            chartClips.Reload();
            foreach (var error in chartClips.Errors) Logger.Warning($"[Battle.BadClip] {error}");
        }

        _music.Clear();
        if (chart.Music != null)
        {
            var song = request.AssetFolder == null ? null : ChartFiles.Inside(request.AssetFolder, chart.Music.File);
            if (song == null) Logger.Warning($"[Music.Missing] {chart.Name}: no folder to find {chart.Music.File} in - playing without music");
            else _music.Load(song);
        }

        var arena = request.Arena;
        Logger.Info($"[Battle] {chart.Name} in the {arena}{(chart.Music != null ? $" to {chart.Music.File}" : "")}: {chart.Actors.Count} actor(s), {chart.Events.Count} events, {chart.End:0.#}s");

        Run = new BattleRun(request, chartClips);
        if (arena.IsLoaded()) Prepare();
        else arena.Load();
    }

    public void Stop(string reason)
    {
        if (Run == null) return;

        Logger.Info($"[Battle.Stopped] {reason}");
        _player.RestoreInvincibility();
        if (Run.State == BattleState.Running && !Run.Request.Recording) BattlePlayer.FullHealth();
        Signals.End();
        actors.DespawnAll();
        _projectiles.Clear();
        _arena.Destroy();
        _board.Hide();
        _music.Clear();
        _moves.AllowMoves();
        _moves.Reset();
        Run = null;
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
        if (Run == null) return;

        if (Run.State == BattleState.LoadingArena && string.Equals(scene, Run.Request.Arena.Scene(), StringComparison.Ordinal))
        {
            MelonCoroutines.Start(PrepareAfterSettling(Run));
            return;
        }

        Logger.Info($"[Battle.Abandoned] left for {scene} mid-battle");
        Run = null;
    }

    public void Update()
    {
        _board.Update();
        if (Run is not { } run) return;

        switch (run.State)
        {
            case BattleState.SpawningActors:
                if (actors.Actors.All(a => a.Ready) || Time.time >= run.SpawnDeadline) BeginCountdown();
                break;

            case BattleState.Countdown:
                KeepLane();
                var left = run.CountdownEnds - Time.time;
                if (left > 0f) _board.Show($"{run.Chart.Name}\n{Mathf.CeilToInt(left)}");
                else if (_music.Loading && Time.time < run.MusicDeadline) _board.Show($"{run.Chart.Name}\nloading music {_music.Progress * 100:0}%");
                else BeginRunning();
                break;

            case BattleState.Running:
                KeepLane();
                _projectiles.Update();
                if (!run.Request.Recording) _player.KeepVulnerable();
                Signals.Update(run.Chart, ChartTime);
                _sweeper.Update(run.Chart.Rules.CleanupSeconds);
                foreach (var command in run.Runner!.Advance(Math.Max(0, ChartTime)))
                {
                    Execute(command);
                    if (Run?.State != BattleState.Running) break;
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
        if (Run is not { State: BattleState.Running } run || run.Request.Recording) return;

        run.Score.PlayerDamaged(amount);
        Logger.Debug($"[Battle.YouWereHit] -{amount}, {healthNow} left");
        Signals.Fire("hit", BattlePlayer.Head, Mathf.Clamp01(amount / 5f), 0.6f);
        if (healthNow <= 0) Finish(survived: false, ChartTime);
    }

    public void OnActorDamaged(int amount)
    {
        if (Run is not { State: BattleState.Running } run) return;
        run.Score.ActorDamaged(amount, run.Phase.ScoreMultiplier);
    }

    private IEnumerator PrepareAfterSettling(BattleRun run)
    {
        // The match maps finish setting themselves up over the first frames.
        yield return new WaitForSeconds(ArenaSettleSeconds);
        if (run == Run && run.State == BattleState.LoadingArena) Prepare();
    }

    // Lays the battle out: the lane, the board, you, and the actors on their spots.
    private void Prepare()
    {
        try
        {
            var request = Run!.Request;
            var chart = Run!.Chart;
            var frame = request.Lane ?? chart.Arena.CreateLaneAnchor();
            if (frame == null)
            {
                Stop("no lane could be laid out");
                return;
            }

            Run.Frame = frame.Value;
            StructureSweeper.ClearAll();
            var performer = request.Performer;

            if (request.Recording)
            {
                _player.Place(World(performer.At.X, 0f, performer.At.Z), Quaternion.Euler(0f, Run.Frame.ActorYaw(performer.At.Facing), 0f));
            }
            else
            {
                _player.Place(Run.Frame.Position.ToUnity(), Quaternion.LookRotation(Run.Frame.Forward.ToUnity()));
            }

            _arena.Build(Run.Frame, chart.Lane, showBackstop: true, walls: true, zone: request.Recording ? null : chart.Zone,
                colours: chart.ColoursIn(chart.Phases.LastOrDefault(p => p.Start <= 0)), panes: chart.EverShowsWalls);
            if (request.Recording)
            {
                _board.SetPosition(World(performer.At.X, 2.6f, Math.Max(0f, performer.At.Z - 3f)), World(performer.At.X, 0f, performer.At.Z));
                _board.Show($"Recording {performer.Name}\nget ready");
            }
            else
            {
                _board.SetPosition(World(0f, 2.6f, Math.Min(3f, chart.Lane.Length / 2f)), Run.Frame.Position.ToUnity());
                _board.Show($"{chart.Name}\n{(request.Mode == BattleMode.Review ? "clip review" : "get ready")}");
            }

            foreach (var spec in request.Recording ? Enumerable.Empty<ActorSpec>() : chart.Actors)
            {
                var actor = new Actor(spec.Id, spec.Name, spec.Look, spec.Shiftstones) { SelfKnockbackMultiplier = 0f, Measurement = spec.Measurement };
                var spot = World(spec.At.X, 0f, spec.At.Z);
                var yaw = Run.Frame.ActorYaw(spec.At.Facing);

                // Spawning puts the head on the floor, so the body falls until placed properly.
                var run = Run;
                actor.OnReady += ready => MelonCoroutines.Start(PlaceWhenSettled(ready, spot, yaw, run));
                actors.Spawn(actor, spot, Quaternion.Euler(0f, yaw, 0f));
            }

            Run.SpawnDeadline = Time.time + ActorSpawnTimeoutSeconds;
            Run.State = BattleState.SpawningActors;
        }
        catch (Exception e)
        {
            Logger.Error($"[Battle.PrepareFailed] {e}");
            Stop("preparing failed");
        }
    }

    private IEnumerator PlaceWhenSettled(Actor actor, Vector3 spot, float yaw, BattleRun run)
    {
        yield return new WaitForSecondsRealtime(ActorSettleSeconds);
        if (run == Run && actor.Ready && run.State is BattleState.SpawningActors or BattleState.Countdown) actor.Body.Footing.Place(spot, yaw);
    }

    private void KeepLane()
    {
        if (Run is null) return;
        _arena.Visuals.Tick();
        _arena.Boundary.LetStructuresThrough();
        _player.KeepInLane(Run.Frame, Run.Chart, zoned: !Run.Request.Recording);
    }

    private void BeginCountdown()
    {
        if (Run is null) return;
        var ready = actors.Actors.Count(a => a.Ready);
        if (ready < actors.Actors.Count) Logger.Warning($"[Battle.ActorsLate] {actors.Actors.Count - ready} of {actors.Actors.Count} actor(s) not ready after {ActorSpawnTimeoutSeconds}s - starting anyway");

        Run.CountdownEnds = Time.time + CountdownSeconds;
        Run.MusicDeadline = Run.CountdownEnds + MusicWaitSeconds;
        _arena.Boundary.LetActorsThrough(actors.Actors.Where(a => a.Ready).SelectMany(a => a.Body!.Collisions.All));
        _moves.DisableMoves();
        Signals.PreloadChartPacks(Run.Chart);
        Run.State = BattleState.Countdown;
    }

    private void BeginRunning()
    {
        if (Run is null) return;
        var request = Run.Request;
        var chart = Run.Chart;

        // Recording runs only the song and phases, with no end; miming keeps the projectiles too.
        Run.Runner = new ChartRunner(request.Recording
            ? chart with
            {
                Events = request.Miming ? chart.Events.OfType<ProjectileEvent>().ToArray() : [],
                Tracks = [],
                End = double.MaxValue,
            }
            : chart);
        Run.StartedAt = Time.time;
        if (_music.Loading) Logger.Warning($"[Music.Late] still loading after {MusicWaitSeconds}s - playing without music");
        if (chart.Music != null) _music.Play(chart.Music.Start, chart.Music.Volume);
        _board.Show("GO", 1f);
        foreach (var actor in actors.Actors) actor.FullHeal();
        _sweeper.Reset();
        Signals.Begin(chart);

        if (!request.Recording)
        {
            BattlePlayer.FullHealth();
            _player.KeepVulnerable();
        }

        // Miming keeps your moves off: you throw the chart's shots by hand.
        if (!request.Miming) _moves.AllowMoves(request.Recording ? SpawnRule.All : chart.Rules.Spawn);
        Run.State = BattleState.Running;
    }

    private void Execute(ChartCommand command)
    {
        if (Run is null) return;
        try
        {
            switch (command)
            {
                case EnterPhase enter:
                    Run.Phase = enter.Phase;
                    if (enter.Time > 0 && settings.ShowPhaseBanners.Value) _board.Show($"{enter.Phase.Name}{(enter.Phase.ScoreMultiplier != 1 ? $"\nx{enter.Phase.ScoreMultiplier:0.#} damage" : "")}", PhaseBannerSeconds);
                    Logger.Debug($"[Battle.Phase] {enter.Phase.Name} at {enter.Time:0.0}s");
                    if (enter.Time > 0) Signals.Fire("phase", World(0f, 0f, 0f), 1f, 1.5f);
                    _arena.Visuals.Recolour(Run.Chart.ColoursIn(enter.Phase), enter.Time > 0 ? PhaseColourFadeSeconds : 0f);
                    break;

                case PlaceActor place:
                    if (Run.Request.Recording) break;

                    // A clip holds its actor to the recorded path, so a move elsewhere would be dragged back.
                    var placed = ActorById(place.ActorId)?.Body;
                    if (placed is { Clips.Playing: true }) Logger.Info($"[Battle.PlaceDuringClip] {place.ActorId} is playing a clip at {place.Time:0.0}s - its \"at\" is skipped");
                    else placed?.Footing.Place(World(place.At.X, 0f, place.At.Z), Run.Frame.ActorYaw(place.At.Facing));
                    break;

                case SpawnProjectile spawn:
                    _projectiles.Spawn(spawn.Id, spawn.Projectile, Run.Frame);
                    Signals.Fire("spawn", World(spawn.Projectile.From.X, spawn.Projectile.From.Y, spawn.Projectile.From.Z), 1f, (float)Math.Max(0.2, spawn.Projectile.Telegraph));
                    break;

                case LaunchProjectile launch:
                    _projectiles.Launch(launch.Id, launch.Projectile, AimPoint(launch.Projectile.To));
                    Signals.Fire("launch", World(launch.Projectile.From.X, launch.Projectile.From.Y, launch.Projectile.From.Z), 1f, 0.5f);
                    Run.Score.ProjectileLaunched();
                    break;

                case RunMove move:
                    ActorById(move.ActorId)?.RunMove(move.Stack);
                    break;

                case PlayClip play:
                    var clip = Run.FindClip(play.Clip);
                    if (clip == null) Logger.Warning($"[Battle.NoClip] no clip called \"{play.Clip}\" in the chart's clips");
                    else ActorById(play.ActorId)?.Body?.Clips.Play(clip, play.From, play.To, Run.Frame, () => ChartTime, play.Mime);
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
        return aim.AtPlayer && (Run?.Request.Miming ?? false)
            ? World(0f, 1.3f, 0f)
            : aim.AtPlayer && BattlePlayer.Player?.Controller is { } playerController
            ? PlayerRig.From(playerController).Head.position + (Vector3.down * 0.25f)
            : World(aim.X, aim.Y, 0f);
    }

    private void Finish(bool survived, double seconds)
    {
        if (Run is not { State: BattleState.Running } run) return;

        var result = run.Score.Finish(survived, seconds, run.Chart.End);
        run.State = BattleState.Finished;
        _music.FadeOut();
        _moves.Reset();
        _player.RestoreInvincibility();
        if (!run.Request.Recording) BattlePlayer.FullHealth();
        Signals.Fire(survived ? "survived" : "defeated", BattlePlayer.Head, 1f, 2f);
        Signals.End();

        var scoreKey = run.Request.ScoreKey;
        var previousBest = scoreKey == null ? null : scores.Get(scoreKey);
        var isNewBest = scoreKey != null && (previousBest == null || previousBest.IsBeatenBy(result));
        try
        {
            if (isNewBest) scores.Set(scoreKey!, BestScore.From(result, DateTime.Now));
        }
        catch (IOException e)
        {
            Logger.Error($"[Scores.SaveFailed] {e.Message}");
        }

        _board.ShowResult(result, isNewBest ? "NEW BEST" : previousBest == null ? null : $"best {previousBest.Rank} {previousBest.Score}");
        Logger.Info(
            $"[Battle] {result.Chart}: {(result.Survived ? "survived" : "defeated")} - rank {result.Rank}, score {result.Score}; " +
            $"damage taken {result.DamageTaken} in {result.HitsTaken} hits, boss damage {result.ActorDamage:0.#}, " +
            $"{result.ProjectilesLaunched} projectiles in {result.Seconds:0.0}s");

        foreach (var actor in actors.Actors)
        {
            if (actor.Ready) actor.Body.Footing.AllowFreePlacement();
        }
        Finished?.Invoke(result);
    }

    private Actor? ActorById(string id)
    {
        var actor = actors.Actors.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));
        if (actor is { Ready: true }) return actor;

        Logger.Warning($"[Battle.NoActor] \"{id}\" is not in play");
        return null;
    }

    private Vector3 World(float x, float y, float z) => Run!.Frame.ToWorld(new System.Numerics.Vector3(x, y, z)).ToUnity();
}
