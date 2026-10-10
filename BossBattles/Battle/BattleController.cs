using System.Collections;
using BossBattles.Actors;
using BossBattles.Core;
using BossBattles.Services.Charts;
using BossBattles.Services.Clips;
using BossBattles.Services.Scoring;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppRUMBLE.Poses;
using MelonLoader;
using UnityEngine;

namespace BossBattles.Battle;

/// <summary>Where a battle is in its life.</summary>
public enum BattleState
{
    Idle,
    LoadingArena,
    SpawningActors,
    Countdown,
    Running,
    Finished,
}

/// <summary>How a battle is run.</summary>
public enum BattleMode
{
    /// <summary>A battle: survive it.</summary>
    Play,

    /// <summary>Recording a take: you stand in the actor's place; no actors, no projectiles, no end - it runs until stopped.</summary>
    Record,

    /// <summary>Watching a take just recorded, as a player would face it - not scored.</summary>
    Review,
}

/// <summary>
/// Runs a battle: loads the arena if it needs one, lays the lane out, spawns and plants the chart's
/// actors, counts down, then plays the chart - each <see cref="ChartCommand"/> the
/// <see cref="ChartRunner"/> hands back is executed here, and that is all the game side does. You
/// win by lasting to the end; your health hitting 0 loses. Scoring is <see cref="ScoreKeeper"/>.
/// </summary>
internal sealed class BattleController
{
    private const float ArenaSettleSeconds = 1.5f;
    private const float ActorSpawnTimeoutSeconds = 15f;
    private const float PhaseBannerSeconds = 2f;

    /// <summary>How long the lane takes to change to a new phase's colours.</summary>
    private const float PhaseColourFadeSeconds = 1f;
    /// <summary>A long MP3 can take a while to decode; the countdown waits this long for it, showing how far it is.</summary>
    private const float MusicWaitSeconds = 90f;

    private readonly LaneVisuals _lane = new();
    private readonly Projectiles _projectiles = new();
    private PlayerHealth? _invincibleBefore;
    private readonly ReShade _reshade = new();
    private long _lastBeat = -1;
    private readonly Board _board = new();
    private readonly List<Actor> _actors = [];
    private readonly Music _music = new();
    private ArenaSpec _arenaSpec = ArenaSpec.Here;
    private ClipLibrary? _chartClips;
    private string? _scoreKey;
    private float _musicDeadline;
    private LaneFrame? _fixedFrame;
    private string? _recordActor;
    private Clip? _pendingTake;
    private PlayerController? _movesHeldFor;
    private float _nextLaneCheck;
    private System.Numerics.Vector2? _recordSpot;
    private LaneFrame _frame;
    private ChartRunner? _runner;
    private ScoreKeeper? _score;
    private Phase? _phase;
    private float _startedAt;
    private float _countdownEnds;
    private float _spawnDeadline;
    private int _session;

    public BattleState State { get; private set; } = BattleState.Idle;

    public Chart? Chart { get; private set; }

    public ArenaKind CurrentArena { get; private set; }

    public BattleResult? LastResult { get; private set; }

    /// <summary>Seconds to count down before a chart starts.</summary>
    public float CountdownSeconds { get; set; } = 3f;

    /// <summary>Knockback each actor takes in a battle - 0 lets a boss stand its ground.</summary>
    public float ActorKnockbackTaken { get; set; }

    public bool ShowBackstop { get; set; } = true;

    /// <summary>Recording a mimed take: the chart's projectiles fly, your moves stay off throughout.</summary>
    public bool Miming { get; private set; }

    /// <summary>Show each new phase's name (and score multiplier) on the board.</summary>
    public bool ShowPhaseBanners { get; set; } = true;

    /// <summary>While true the match handler is stopped from sending you back to the Gym (see ArenaPatches).</summary>
    public bool HoldsArena => State != BattleState.Idle && Arena.IsMatchMap(CurrentArena);

    /// <summary>Your music volume, 0 to 1 (the setting); a chart's own volume is applied on top.</summary>
    public float MusicVolume
    {
        get => _music.Volume;
        set => _music.Volume = value;
    }

    /// <summary>The song's state for the battle in progress - the self-test reads it.</summary>
    public MusicState MusicState => _music.State;

    /// <summary>Where best scores are kept; null keeps none.</summary>
    public ScoreBook? Scores { get; set; }

    /// <summary>Whether the last result beat the best before it.</summary>
    public bool LastWasBest { get; private set; }

    /// <summary>Seconds into the chart: the song's clock when there is a song, else game time. Negative or meaningless before GO.</summary>
    public double ChartTime => State is BattleState.Running or BattleState.Finished ? StartAt + (_music.Clock ?? (Time.time - _startedAt)) : -1;

    /// <summary>Chart time the battle started from - 0, or part-way for a section.</summary>
    public double StartAt { get; private set; }

    public BattleMode Mode { get; private set; } = BattleMode.Play;

    /// <summary>The lane as laid out, once it is.</summary>
    public LaneFrame? Lane => State is BattleState.Idle or BattleState.LoadingArena || Chart == null ? null : _frame;

    /// <summary>Shows a line on the board for a while - a count-in.</summary>
    public void Banner(string text, float seconds) => _board.Show(text, seconds);

    /// <summary>Whether the song has played to its end - a recording stops there.</summary>
    public bool SongOver => Chart?.Music != null && _music.Ended;

    /// <summary>The battle's actors - the self-test reads them.</summary>
    public IReadOnlyList<Actor> Actors => _actors;

    /// <summary>Projectile counts for the battle in progress (or the last one) - the self-test reads them.</summary>
    public (int Spawned, int Launched, int Failed) ProjectileCounts => (_projectiles.Spawned, _projectiles.Launched, _projectiles.Failed);

    /// <summary>Grounded projectiles thrown along the floor this battle - the self-test reads it.</summary>
    public int GroundedLaunched => _projectiles.GroundedLaunched;

    /// <summary>The last grounded projectile thrown - the self-test parries and hitstops it.</summary>
    public Rigidbody? LastGroundedBody => _projectiles.LastGroundedBody;

    /// <summary>The lane's real length in metres once laid out - the self-test reads it.</summary>
    public float LaneMetres => Chart == null ? 0f : Chart.Lane.Length * _frame.DepthScale;

    /// <summary>
    /// A viewpoint over the lane once it is laid out: up behind you, over the backstop, looking down
    /// the lane - for the self-test's screenshots.
    /// </summary>
    public (Vector3 Position, Vector3 LookAt)? Overview =>
        State is BattleState.Idle or BattleState.LoadingArena || Chart == null
            ? null
            : (World(1.5f, 3.6f, -0.4f), World(0f, 0.8f, Chart.Lane.Length * 0.6f));

    /// <summary>The centre of your end of the lane, once laid out - the self-test checks you were moved there.</summary>
    public Vector3? LaneOrigin => State is BattleState.Idle or BattleState.LoadingArena || Chart == null ? null : World(0f, 0f, 0f);

    /// <summary>A world point in lane coordinates (x across, z along, chart units) - the self-test checks you stayed inside.</summary>
    public Vector3 ToLane(Vector3 world) => Describe.ToUnity(_frame.ToLane(Describe.ToNumerics(world)));

    /// <summary>The lane's chart size, or null with no chart.</summary>
    public Lane? LaneSize => Chart?.Lane;

    /// <summary>For a battle "here": the lane runs this way instead of the way you face. Cleared when a battle starts laying out.</summary>
    public Vector3? HereFacing { get; set; }

    /// <summary>Where <c>clip</c> events find their clips.</summary>
    public ClipLibrary? Clips { get; set; }

    /// <summary>How many of the battle's actors came up ready - the self-test reads it.</summary>
    public int ReadyActors => _actors.Count(a => a.Ready);

    /// <summary>Raised when a battle finishes, survived or not.</summary>
    public event Action<BattleResult>? Finished;

    // ---- Control ---------------------------------------------------------------------------------

    /// <summary>Plays a chart.</summary>
    /// <param name="chart">The chart.</param>
    /// <param name="arenaOverride">An arena to play in instead of the chart's own.</param>
    /// <param name="assetFolder">Where its song and <c>clips/</c> are.</param>
    /// <param name="scoreKey">Files the result in <see cref="Scores"/>; null keeps no score.</param>
    /// <param name="recordActor">The actor recorded (default the first).</param>
    /// <param name="mode">Play, record a take (as <paramref name="recordActor"/>), or review one.</param>
    /// <param name="lane">A lane already laid out - a take's - instead of laying one now.</param>
    /// <param name="pendingTake">A take not yet saved, found by name before the chart's clips.</param>
    /// <param name="startAt">Chart time to start from - a section.</param>
    /// <param name="recordSpot">Recording: where in the lane to stand (lane x, z) instead of the actor's spot.</param>
    /// <param name="mime">Recording a mimed take: the chart's projectiles fly, and your moves stay off.</param>
    public void Start(
        Chart chart,
        ArenaKind? arenaOverride = null,
        string? assetFolder = null,
        string? scoreKey = null,
        BattleMode mode = BattleMode.Play,
        string? recordActor = null,
        LaneFrame? lane = null,
        Clip? pendingTake = null,
        double startAt = 0,
        System.Numerics.Vector2? recordSpot = null,
        bool mime = false)
    {
        Miming = mode == BattleMode.Record && mime;
        ArenaKind arena = arenaOverride ?? chart.Arena.Map;
        if (State != BattleState.Idle) Stop("a new battle was started");
        if (PlayerManager.instance?.localPlayer?.Controller == null)
        {
            Mod.Note("Battle.NotStarted", "no local player yet");
            return;
        }

        Chart = chart;
        CurrentArena = arena;
        Mode = mode;
        _recordActor = recordActor ?? chart.DefaultActor.Id;
        _fixedFrame = lane;
        _pendingTake = pendingTake;
        StartAt = Math.Max(0, startAt);
        _recordSpot = recordSpot;
        // An overridden arena keeps the chart's spot only if it is the chart's own map.
        _arenaSpec = arena == chart.Arena.Map ? chart.Arena : new ArenaSpec(arena);
        _scoreKey = scoreKey;
        _session++;
        _projectiles.ResetCounts();
        LastResult = null;
        LastWasBest = false;

        _chartClips = null;
        var clipFolder = assetFolder == null ? null : Path.Combine(assetFolder, ChartPackage.ClipsFolder);
        if (clipFolder != null && Directory.Exists(clipFolder))
        {
            _chartClips = new ClipLibrary(clipFolder);
            _chartClips.Reload();
            foreach (var error in _chartClips.Errors) Mod.Note("Battle.BadClip", error);
        }

        _music.Clear();
        if (chart.Music != null)
        {
            var song = assetFolder == null ? null : ChartPaths.Inside(assetFolder, chart.Music.File);
            if (song == null) Mod.Note("Music.Missing", $"{chart.Name}: no folder to find {chart.Music.File} in - playing without music");
            else _music.Load(song);
        }

        foreach (var missing in chart.Events.OfType<ClipEvent>().Select(c => c.Clip).Distinct().Where(c => FindClip(c) == null))
            Mod.Note("Battle.NoClip", $"{chart.Name} plays a clip called \"{missing}\" that is not in the clip folder - it will be skipped");

        Mod.Say($"[Battle] {chart.Name} in {(arena == ArenaKind.Here ? "place" : $"the {arena}")}{(chart.Music != null ? $" to {chart.Music.File}" : "")}: {chart.Actors.Count} actor(s), {chart.Events.Count} events, {chart.End:0.#}s");

        if (Arena.InScene(arena))
        {
            // Already there - but setting up all the same, not idle: actors ready during Prepare
            // are placed only while a battle is being set up.
            State = BattleState.LoadingArena;
            Prepare();
            return;
        }

        State = BattleState.LoadingArena;
        Arena.Load(arena);
    }

    public void Stop(string reason)
    {
        if (State == BattleState.Idle) return;

        Mod.Note("Battle.Stopped", reason);
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

    /// <summary>Back to the Gym from an arena, ending any battle.</summary>
    public void ReturnToGym()
    {
        Stop("returning to the Gym");
        Arena.LoadGym();
    }

    public void OnSceneLoaded(string scene)
    {
        // Everything built in the old scene went with it - your player too.
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
            Mod.Note("Battle.Abandoned", $"left for {scene} mid-battle");
            _runner = null;
            State = BattleState.Idle;
        }
    }

    /// <summary>
    /// Moves you onto a floor point with the game's <c>Reposition</c>, then checks where your feet
    /// landed and corrects once. Reposition places the head, and turning you round swings the
    /// head's offset from the rig with it: recording, turned to face back down the lane, first
    /// stood you 1.9 m from the actor's spot.
    /// </summary>
    private void PlaceYou(PlayerController you, Vector3 floor, Quaternion facing, float eyeHeight)
    {
        you.PlayerMovement.Reposition(floor + (Vector3.up * eyeHeight), facing);
        MelonCoroutines.Start(CorrectPlacement(you, floor, facing, eyeHeight, _session));
    }

    private IEnumerator CorrectPlacement(PlayerController you, Vector3 floor, Quaternion facing, float eyeHeight, int session)
    {
        yield return null;
        yield return null;
        if (session != _session || you == null) yield break;

        Vector3 feet = ActorRig.Of(you).FloorPoint;
        var off = new Vector3(floor.x - feet.x, 0f, floor.z - feet.z);
        if (off.magnitude > 0.15f)
        {
            you.PlayerMovement.Reposition(floor + off + (Vector3.up * eyeHeight), facing);
            yield return null;
            yield return null;
            if (session != _session || you == null) yield break;
            feet = ActorRig.Of(you).FloorPoint;
        }

        var left = new Vector2(floor.x - feet.x, floor.z - feet.z).magnitude;
        Mod.Note(left > 0.3f ? "Battle.YouPlacedOff" : "Battle.YouPlaced", $"{Describe.Vector(feet)}, {left:0.00} m from {Describe.Vector(floor)} (first try {off.magnitude:0.00} m off)", echo: left > 0.3f);
    }

    /// <summary>
    /// A new actor put on its spot properly, a moment after it is ready: placed in the frame it
    /// spawned, its body had not settled and went on falling (2.3 m low a third of a second later).
    /// </summary>
    private IEnumerator PlaceWhenSettled(Actor actor, Vector3 spot, float yaw, int session)
    {
        yield return new WaitForSecondsRealtime(ActorSettleSeconds);
        if (session == _session && actor.Exists && State is not (BattleState.Idle or BattleState.Running or BattleState.Finished)) actor.Place(spot, yaw);
    }

    private const float ActorSettleSeconds = 0.1f;

    private IEnumerator PrepareAfterSettling(int session)
    {
        // The match maps finish setting themselves up over the first frames.
        yield return new WaitForSeconds(ArenaSettleSeconds);
        if (session == _session && State == BattleState.LoadingArena) Prepare();
    }

    private void Prepare()
    {
        try
        {
            PlayerController you = PlayerManager.instance.localPlayer.Controller;
            var yours = ActorRig.Of(you);

            Vector3 facing = HereFacing ?? yours.Facing;
            HereFacing = null;
            LaneFrame? frame = _fixedFrame ?? Arena.Frame(CurrentArena, _arenaSpec, Chart!.Lane, yours.FloorPoint, facing);
            if (frame == null)
            {
                Stop("no lane could be laid out");
                return;
            }

            _frame = frame.Value;
            ClearStructures();

            // Reposition places the head, so the floor point is lifted by your eye height.
            var eyeHeight = yours.Head.position.y - yours.Vr.position.y;
            ActorSpec performer = Chart!.Actors.FirstOrDefault(a => a.Id == _recordActor) ?? Chart.DefaultActor;

            if (Mode == BattleMode.Record)
            {
                // In the actor's place, facing back down the lane, as the actor will stand.
                // A section starts where the actor stood at that moment of its take.
                Vector3 spot = _recordSpot is { } at ? World(at.X, 0f, at.Y) : World(performer.At.X, 0f, performer.At.Z);
                PlaceYou(you, spot, Quaternion.Euler(0f, _frame.ActorYawDegrees(performer.At.Facing), 0f), eyeHeight);
            }
            else if (Arena.IsMatchMap(CurrentArena) || _arenaSpec.HasSpot || _fixedFrame != null)
            {
                // Onto your spot, facing down the lane.
                PlaceYou(you, Describe.ToUnity(_frame.Origin), Quaternion.LookRotation(Describe.ToUnity(_frame.Forward)), eyeHeight);
            }

            _lane.Build(_frame, Chart.Lane, ShowBackstop, walls: true, zone: Mode == BattleMode.Record ? null : Chart.Zone,
                colours: Chart.ColoursIn(Chart.Phases.LastOrDefault(p => p.Start <= StartAt)), panes: Chart.EverShowsWalls);
            if (Mode == BattleMode.Record)
            {
                // Between you and the lane's start, readable from the actor's end.
                _board.Place(World(performer.At.X, 2.6f, Math.Max(0f, performer.At.Z - 3f)), World(performer.At.X, 0f, performer.At.Z));
                _board.Show($"<b>Recording {performer.Name}</b>\nget ready");
            }
            else
            {
                _board.Place(World(0f, 2.6f, Math.Min(3f, Chart.Lane.Length / 2f)), Describe.ToUnity(_frame.Origin));
                _board.Show($"<b>{Chart.Name}</b>\n{(Mode == BattleMode.Review ? "take review" : "get ready")}");
            }

            foreach (ActorSpec spec in Mode == BattleMode.Record ? Enumerable.Empty<ActorSpec>() : Chart.Actors)
            {
                var actor = new Actor(spec.Id, spec.Name, spec.Look, stones: spec.Shiftstones) { KnockbackTaken = ActorKnockbackTaken };
                actor.WasKnockedOut += OnActorKnockedOut;
                _actors.Add(actor);
                Vector3 spot = World(spec.At.X, 0f, spec.At.Z);
                var yaw = _frame.ActorYawDegrees(spec.At.Facing);

                // Spawning puts the head where it is told - here the floor - so the body starts under
                // it and fell through during the countdown (7 m in the look test) until GO placed it.
                // Placed properly, feet on the floor and planted, the moment it is ready.
                var session = _session;
                actor.BecameReady += ready => MelonCoroutines.Start(PlaceWhenSettled(ready, spot, yaw, session));
                actor.Spawn(spot, Quaternion.Euler(0f, yaw, 0f));
            }

            _spawnDeadline = Time.time + ActorSpawnTimeoutSeconds;
            State = BattleState.SpawningActors;
        }
        catch (Exception e)
        {
            Mod.Note("Battle.PrepareFailed", e.ToString());
            Stop("preparing failed");
        }
    }

    // ---- Per frame -------------------------------------------------------------------------------

    public void Update()
    {
        _board.Update();

        switch (State)
        {
            case BattleState.SpawningActors:
                if (_actors.All(a => a.Ready) || Time.time >= _spawnDeadline) BeginCountdown();
                break;

            case BattleState.Countdown:
                _lane.Tick();
                _lane.LetStructuresThrough();
                KeepYouInLane();
                var left = _countdownEnds - Time.time;
                if (left > 0f) _board.Show($"<b>{Chart!.Name}</b>\n{Mathf.CeilToInt(left)}");
                else if (_music.State == MusicState.Loading && Time.time < _musicDeadline) _board.Show($"<b>{Chart!.Name}</b>\nloading music {_music.Progress * 100:0}%");
                else BeginRunning();
                break;

            case BattleState.Running:
                _lane.Tick();
                _lane.LetStructuresThrough();
                KeepYouInLane();
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
                _lane.Tick();
                _lane.LetStructuresThrough();
                KeepYouInLane();
                _projectiles.Update();
                break;
        }
    }

    private void BeginCountdown()
    {
        var ready = _actors.Count(a => a.Ready);
        if (ready < _actors.Count) Mod.Note("Battle.ActorsLate", $"{_actors.Count - ready} of {_actors.Count} actor(s) not ready after {ActorSpawnTimeoutSeconds}s - starting anyway");

        foreach (Actor actor in _actors.Where(a => a.Exists))
        {
            actor.SetControl(ActorControl.Bot);
            actor.Input.FaceYou = false;
        }

        _countdownEnds = Time.time + CountdownSeconds;
        _musicDeadline = _countdownEnds + MusicWaitSeconds;
        _lane.LetActorsThrough(_actors.SelectMany(a => a.Colliders));
        HoldYourMoves();
        ReShade.Refresh();
        _reshade.Preload(Chart!.Visuals.Select(v => v.Pack));
        State = BattleState.Countdown;
    }

    /// <summary>
    /// No moves until GO: your gesture recognition is off through the countdown - the same switch an
    /// actor's take uses - so nothing is spawned early. Back on at GO, on a stop, or with a new scene
    /// (a new player has its own).
    /// </summary>
    private void HoldYourMoves()
    {
        PlayerController? you = PlayerManager.instance?.localPlayer?.Controller;
        if (you == null || _movesHeldFor != null) return;

        try
        {
            you.PlayerProcessor.RemoveAllStackInputSources();
            _movesHeldFor = you;
            Mod.Note("Battle.MovesHeld", "until GO", echo: false);
        }
        catch (Exception e)
        {
            Mod.Note("Battle.MovesNotHeld", e.Message);
        }
    }

    /// <summary>The structures players spawn - what a new battle clears off the map (scenery stays).</summary>
    private static readonly string[] PlayerStructures = { "Disc", "Ball", "RockCube", "Pillar", "Wall" };

    /// <summary>
    /// A battle starts on a clear floor: every structure a player spawned is broken with the game's
    /// own <c>Structure.Kill</c> (its break effect, no sound) and goes back to its pool.
    /// </summary>
    /// <summary>How many structures the last battle cleared off the map - the self-test reads it.</summary>
    public static int LastCleared { get; private set; }

    private static void ClearStructures()
    {
        CombatManager? combat = CombatManager.instance;
        if (combat == null) return;

        var cleared = 0;
        foreach (Structure structure in combat.Structures.ToArray())
        {
            if (structure == null || !structure.gameObject.activeInHierarchy) continue;

            var name = structure.gameObject.name;
            if (!PlayerStructures.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;

            try
            {
                structure.Kill(Vector3.zero, false, true, false);
                cleared++;
            }
            catch (Exception e)
            {
                Mod.Note("Battle.ClearFailed", $"{name}: {e.Message}");
            }
        }

        LastCleared = cleared;
        Mod.Note("Battle.Cleared", $"{cleared} structure(s) cleared off the map", echo: false);
    }

    /// <summary>The spawn moves, and what each spawns - which a chart's spawn rule takes away.</summary>
    private static readonly string[] SpawnMoves = { "Disc", "SpawnBall", "SpawnCube", "SpawnPillar", "SpawnWall" };

    private readonly List<Il2CppRUMBLE.MoveSystem.Stack> _withheld = [];
    private PlayerController? _withheldFrom;

    /// <summary>Your moves back on at GO - without the spawn moves the chart's rule disallows.</summary>
    private void ReleaseYourMoves()
    {
        // Miming: no structures from you at all - the moves stay off until the recording stops.
        if (Miming && State == BattleState.Countdown) return;

        PlayerController? you = _movesHeldFor;
        _movesHeldFor = null;
        if (you == null) return;

        try
        {
            if (State == BattleState.Countdown && Mode != BattleMode.Record && Chart != null && Chart.Rules.Spawn != SpawnRule.All) Withhold(you, Chart.Rules.Spawn);
            you.PlayerProcessor.InitializeStackInputSources(true);
            if (_withheld.Count > 0) DropWithheldGestures(you);
        }
        catch (Exception e)
        {
            Mod.Note("Battle.MovesNotReleased", $"your moves may stay off until the scene changes: {e.Message}");
        }
    }

    private void Withhold(PlayerController you, SpawnRule rule)
    {
        Il2CppSystem.Collections.Generic.List<Il2CppRUMBLE.MoveSystem.Stack> stacks = you.PlayerProcessor.availableStacks;
        foreach (Il2CppRUMBLE.MoveSystem.Stack stack in stacks.ToArray())
        {
            if (stack == null || !SpawnMoves.Contains(stack.CachedName, StringComparer.OrdinalIgnoreCase)) continue;
            if (rule == SpawnRule.Discs && string.Equals(stack.CachedName, "Disc", StringComparison.OrdinalIgnoreCase)) continue;

            stacks.Remove(stack);
            _withheld.Add(stack);
        }

        _withheldFrom = you;
        Mod.Note("Battle.SpawnsWithheld", $"{rule}: {string.Join(", ", _withheld.Select(s => s.CachedName))}", echo: false);
    }

    /// <summary>
    /// Takes the withheld moves' gestures away. The game builds your gesture inputs from every move
    /// you own (<c>MoveManager.FetchOwnedMoves</c>), not from <c>availableStacks</c> - so taking a
    /// stack off that list alone left its gesture working, and VR players could still spawn on a
    /// discs-only chart. Each gesture is a handle pairing a stack with a pose input: the handle goes,
    /// and its pose input too unless a move still allowed shares it.
    /// </summary>
    private void DropWithheldGestures(PlayerController you)
    {
        Il2CppSystem.Collections.Generic.List<PlayerStackProcessor.StackHandle> handles = you.PlayerProcessor.activeStackHandles;
        PlayerPoseSystem poses = you.PlayerPoseSystem;
        var dropped = new List<string>();

        foreach (PlayerStackProcessor.StackHandle? handle in handles.ToArray())
        {
            if (handle?.Stack == null || !_withheld.Any(s => s.Pointer == handle.Stack.Pointer || string.Equals(s.CachedName, handle.Stack.CachedName, StringComparison.OrdinalIgnoreCase))) continue;

            handles.Remove(handle);
            dropped.Add(handle.Stack.CachedName);

            PoseInputSource? source = handle.InputSource;
            var shared = source != null && handles.ToArray().Any(h => h?.InputSource != null && h.InputSource.Pointer == source.Pointer);
            if (source != null && !shared) poses.RemoveInputSource(source);
        }

        WithheldGesturesLeft = handles.ToArray().Count(h => h?.Stack != null && _withheld.Any(s => string.Equals(s.CachedName, h.Stack.CachedName, StringComparison.OrdinalIgnoreCase)));
        Mod.Note(dropped.Count > 0 ? "Battle.GesturesDropped" : "Battle.NoGesturesFound", $"{(dropped.Count > 0 ? string.Join(", ", dropped) : "no gesture handles for the withheld moves")}; {handles.Count} gesture(s) left", echo: dropped.Count == 0);
    }

    /// <summary>Whether your gestures include a move right now (a handle for it) - the self-test reads it.</summary>
    public static bool HasGesture(string move)
    {
        PlayerController? you = PlayerManager.instance?.localPlayer?.Controller;
        return you != null && you.PlayerProcessor.activeStackHandles.ToArray().Any(h => string.Equals(h?.Stack?.CachedName, move, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Gestures for withheld moves still live after dropping them - the self-test reads it; 0 when right.</summary>
    public int WithheldGesturesLeft { get; private set; }

    /// <summary>The spawn moves back after the battle - a stop, a finish, or (with no player left) a scene change.</summary>
    private void GiveSpawnsBack()
    {
        PlayerController? you = _withheldFrom;
        _withheldFrom = null;
        if (you == null || _withheld.Count == 0)
        {
            _withheld.Clear();
            return;
        }

        try
        {
            you.PlayerProcessor.RemoveAllStackInputSources();
            foreach (Il2CppRUMBLE.MoveSystem.Stack stack in _withheld) you.PlayerProcessor.availableStacks.Add(stack);
            you.PlayerProcessor.InitializeStackInputSources(true);
            Mod.Note("Battle.SpawnsReturned", string.Join(", ", _withheld.Select(s => s.CachedName)), echo: false);
        }
        catch (Exception e)
        {
            Mod.Note("Battle.SpawnsNotReturned", $"your spawn moves may stay off until the scene changes: {e.Message}");
        }

        _withheld.Clear();
    }

    /// <summary>Whether a spawn move is off your list right now - the self-test reads it.</summary>
    public bool Withholds(string move) => _withheld.Any(s => string.Equals(s.CachedName, move, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The walls' safety net: should you get past one anyway, you are put back at the nearest point
    /// inside the lane, facing as you were.
    /// </summary>
    private void KeepYouInLane()
    {
        if (Time.time < _nextLaneCheck || Chart == null) return;
        _nextLaneCheck = Time.time + 0.1f;

        PlayerController? you = PlayerManager.instance?.localPlayer?.Controller;
        if (you == null) return;

        var rig = ActorRig.Of(you);
        Vector3 at = ToLane(rig.FloorPoint);
        var zoned = Mode != BattleMode.Record;
        float side = (Chart.Lane.Width / 2f) + 0.3f, back = -0.75f;
        var far = zoned ? Chart.Zone.Length + (0.3f / _frame.DepthScale) : Chart.Lane.Length + (0.75f / _frame.DepthScale);
        var above = rig.Vr.position.y - Describe.ToUnity(_frame.Origin).y;
        var tooHigh = zoned && above > Chart.Zone.Height + 0.8f;
        if (!tooHigh && Math.Abs(at.x) <= side + 0.3f && at.z >= back - 0.3f && at.z <= far + 0.3f) return;

        // Pushed back, not teleported: both bodies shifted by the way out, height kept and the speed
        // carrying you out stopped. Reposition placed your head at the lane floor plus your eye height,
        // which put you up high when you were mid-air, crouched or on lower ground.
        Vector3 inside = World(Math.Clamp(at.x, -side + 0.2f, side - 0.2f), 0f, Math.Clamp(at.z, back + 0.2f, far - 0.2f));
        Vector3 floor = rig.FloorPoint;
        var shift = new Vector3(inside.x - floor.x, 0f, inside.z - floor.z);
        if (tooHigh) shift.y = -(above - Chart.Zone.Height);
        PlayerPhysics physics = you.PlayerPhysics;
        foreach (Rigidbody? body in new[] { physics.PhysicsRigidbody, physics.VRrigidbody })
        {
            if (body == null) continue;
            body.position += shift;
            Vector3 v = body.linearVelocity;
            Vector3 outward = shift.sqrMagnitude > 1e-6f ? -shift.normalized : Vector3.zero;
            var away = Vector3.Dot(v, outward);
            if (away > 0f) body.linearVelocity = v - (outward * away);
        }

        Mod.Note("Battle.YouLeftTheLane", $"at lane x {at.x:0.0}, z {at.z:0.0}{(tooHigh ? $", {above:0.0} m up" : "")} - put back", echo: false);
    }

    private void BeginRunning()
    {
        // Recording: only the song and the phases run - no actors, no projectiles, and no end. Miming
        // keeps the chart's projectiles too, thrown at your empty end, to pretend to throw.
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
        if (_music.State == MusicState.Loading) Mod.Note("Music.Late", $"still loading after {MusicWaitSeconds}s - playing without music");
        if (Chart.Music != null) _music.Play(Chart.Music.Start + StartAt, Chart.Music.Volume);
        _board.Show("<b>GO</b>", 1f);
        if (Mode != BattleMode.Record) FullHealth();
        foreach (Actor actor in _actors) actor.FullHealth();
        _still.Clear();
        CleanedUp = 0;
        KeepYouVulnerable();
        ReShade.Refresh();
        _lastBeat = -1;
        if (Chart.Visuals.Count > 0) Mod.Note("Visuals.Chart", $"{Chart.Visuals.Count} look(s); installed: {ReShade.Describe()}", echo: false);
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
                    Mod.Note("Battle.Phase", $"{enter.Phase.Name} at {enter.Time:0.0}s", echo: false);
                    if (enter.Time > 0) _reshade.Fire("phase", World(0f, 0f, 0f), 1f, 1.5f);
                    _lane.Recolour(Chart!.ColoursIn(enter.Phase), enter.Time > 0 && enter.Time > StartAt ? PhaseColourFadeSeconds : 0f);
                    break;

                case PlaceActor place:
                    if (Mode == BattleMode.Record) break;

                    // A take holds its actor to the recorded path - a move to another spot would be
                    // dragged straight back (1.3 m over 0.6 s in the first playback check).
                    Actor? placed = ActorById(place.ActorId);
                    if (placed is { PlayingTake: true }) Mod.Note("Battle.PlaceDuringTake", $"{place.ActorId} is playing a take at {place.Time:0.0}s - its \"at\" is skipped");
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

                case PlayTake take:
                    Clip? takeClip = FindClip(take.Clip);
                    if (takeClip == null) Mod.Note("Battle.NoClip", $"no take called \"{take.Clip}\" in the chart's clips");
                    else ActorById(take.ActorId)?.PlayTake(takeClip, take.From, take.Until, _frame, () => ChartTime, take.Mime);
                    break;

                case PlayClip play:
                    Clip? clip = FindClip(play.Clip);
                    if (clip == null) Mod.Note("Battle.NoClip", $"no clip called \"{play.Clip}\" in {Clips?.Folder ?? "the clip folder"}");
                    else ActorById(play.ActorId)?.PlayClip(clip);
                    break;

                case EndChart end:
                    Finish(survived: true, end.Time);
                    break;
            }
        }
        catch (Exception e)
        {
            Mod.Note("Battle.CommandFailed", $"{command}: {e}");
        }
    }

    private Vector3 AimPoint(Aim aim)
    {
        // Miming, you stand at the boss's end: "the player" is the lane's near end, where they would be.
        if (aim.AtPlayer && Miming) return World(0f, 1.3f, 0f);
        if (aim.AtPlayer)
        {
            PlayerController? you = PlayerManager.instance?.localPlayer?.Controller;
            if (you != null) return ActorRig.Of(you).Head.position + (Vector3.down * 0.25f);
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
            Mod.Note("Scores.SaveFailed", e.Message);
        }

        _board.ShowResult(result, LastWasBest ? "NEW BEST" : previous == null ? null : $"best {previous.Rank} {previous.Score}");
        Mod.Say(
            $"[Battle] {result.Chart}: {(result.Survived ? "survived" : "defeated")} - rank {result.Rank}, score {result.Score}; " +
            $"damage taken {result.DamageTaken} in {result.HitsTaken} hits, boss damage {result.ActorDamage:0.#}, knock-outs {result.KnockOuts}, " +
            $"{result.ProjectilesLaunched} projectiles in {result.Seconds:0.0}s");

        foreach (Actor actor in _actors) actor.Unplant();
        Finished?.Invoke(result);
    }

    /// <summary>
    /// Takeable damage for the run: outside a match the Ring and the Pit make you invincible, so the
    /// game's own hits (damage, hit stun, knockback) did nothing there - lifted while running and put
    /// back after. And no health back mid-run: outside a match the game regenerates it.
    /// </summary>
    private void KeepYouVulnerable()
    {
        if (Mode == BattleMode.Record || KeepInvincible) return;

        PlayerController? you = PlayerManager.instance?.localPlayer?.Controller;
        if (you == null) return;

        try
        {
            PlayerHealth health = you.PlayerHealth;
            if (health.IsInvincible)
            {
                _invincibleBefore ??= health;
                health.IsInvincible = false;
                Mod.Note("Battle.InvincibilityLifted", "you were invincible here - lifted for the run", echo: false);
            }

            if (health.currentHealthResetRoutine != null)
            {
                health.StopCoroutine(health.currentHealthResetRoutine);
                health.currentHealthResetRoutine = null;
                Mod.Note("Battle.RegenHeld", "the game's health regeneration stopped for the run", echo: false);
            }
        }
        catch (Exception e)
        {
            Mod.Note("Battle.VulnerableFailed", e.ToString());
        }
    }

    /// <summary>The self-test's playback check: leave your invincibility as the arena has it, so a run lasts the whole chart.</summary>
    public bool KeepInvincible { get; set; }

    /// <summary>Puts back the invincibility <see cref="KeepYouVulnerable"/> lifted.</summary>
    private void RestoreInvincibility()
    {
        if (_invincibleBefore == null) return;

        try
        {
            _invincibleBefore.IsInvincible = true;
        }
        catch (Exception e)
        {
            Mod.Note("Battle.VulnerableFailed", e.Message);
        }

        _invincibleBefore = null;
    }

    private readonly Dictionary<IntPtr, (Vector3 At, float Since)> _still = [];
    private float _nextCleanup;

    /// <summary>Structures broken for sitting still - the self-test reads it.</summary>
    public int CleanedUp { get; private set; }

    /// <summary>
    /// The chart's cleanup rule: a structure that has not moved for <see cref="Rules.CleanupSeconds"/>
    /// breaks, with the game's own effects - so a wall grounded in front of you does not soak up a
    /// whole section, and what a recorded take leaves standing does not clutter the lane. Telegraphing
    /// projectiles (held, kinematic) are left alone.
    /// </summary>
    private void CleanUpStillStructures()
    {
        var after = Chart?.Rules.CleanupSeconds ?? 0;
        if (after <= 0 || Time.time < _nextCleanup) return;
        _nextCleanup = Time.time + 0.25f;

        CombatManager? combat = CombatManager.instance;
        if (combat == null) return;

        var seen = new HashSet<IntPtr>();
        foreach (Structure structure in combat.Structures.ToArray())
        {
            if (structure == null || !structure.gameObject.activeInHierarchy) continue;
            Rigidbody body = structure.GetComponent<Rigidbody>();
            if (body == null || body.isKinematic) continue;

            seen.Add(structure.Pointer);
            Vector3 at = structure.transform.position;
            if (!_still.TryGetValue(structure.Pointer, out (Vector3 At, float Since) still) || (at - still.At).sqrMagnitude > 0.05f * 0.05f)
            {
                _still[structure.Pointer] = (at, Time.time);
                continue;
            }

            if (Time.time - still.Since < after) continue;

            try
            {
                structure.Kill(at, true, true, false);
                CleanedUp++;
                Mod.Note("Battle.CleanedUp", $"{structure.gameObject.name} still for {after:0.#}s - broken", echo: false);
            }
            catch (Exception e)
            {
                Mod.Note("Battle.CleanupFailed", $"{structure.gameObject.name}: {e.Message}");
            }

            _still.Remove(structure.Pointer);
        }

        foreach (IntPtr gone in _still.Keys.Where(k => !seen.Contains(k)).ToList()) _still.Remove(gone);
    }

    /// <summary>The lane's edge colour right now - the self-test reads it.</summary>
    public Color? LaneEdgeColour => _lane.EdgeColourNow;

    /// <summary>How strongly charts' shader looks and battle signals apply (RumbleReShaded): 0 off, 1 full.</summary>
    public float VisualsStrength
    {
        get => _reshade.Strength;
        set => _reshade.Strength = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>
    /// The chart's looks at this moment, and the state packs can read - health, the beat, the
    /// clock - with a <c>beat</c> event on each one (stronger on the first of a bar of four).
    /// </summary>
    private void UpdateVisuals()
    {
        var t = ChartTime;
        _reshade.Apply(Chart!.Visuals, t);

        ReShade.SetGlobal("running", 1f);
        ReShade.SetGlobal("time", (float)t);
        Player? you = PlayerManager.instance?.localPlayer;
        if (you?.Data != null) ReShade.SetGlobal("health", you.Data.HealthPoints / (float)PlayerHealth.MAX_PLAYER_HEALTH);

        if (Chart.Tempo is not { } tempo) return;

        var beat = tempo.Beat(Math.Max(0, t));
        ReShade.SetGlobal("beat", (float)(beat - Math.Floor(beat)));
        var whole = (long)Math.Floor(beat);
        if (whole == _lastBeat) return;

        _lastBeat = whole;
        _reshade.Fire("beat", YourHead(), whole % 4 == 0 ? 1f : 0.6f, (float)(60.0 / tempo.BpmAt(t)));
    }

    /// <summary>Your settings back on every pack a chart touched, and the state globals at rest.</summary>
    private void EndVisuals()
    {
        _reshade.RestoreAll();
        ReShade.SetGlobal("running", 0f);
        ReShade.SetGlobal("beat", 0f);
    }

    /// <summary>For the game quitting mid-run: your RumbleReShaded settings back before MelonLoader saves them.</summary>
    public void RestoreVisuals() => EndVisuals();

    private static Vector3 YourHead()
    {
        PlayerController? you = PlayerManager.instance?.localPlayer?.Controller;
        return you == null ? Vector3.zero : ActorRig.Of(you).Head.position;
    }

    /// <summary>Every run starts on full health, and ends on it - the regeneration it held back.</summary>
    private static void FullHealth()
    {
        Player? you = PlayerManager.instance?.localPlayer;
        if (you?.Controller == null) return;

        var hp = you.Data.HealthPoints;
        if (hp < PlayerHealth.MAX_PLAYER_HEALTH) you.Controller.PlayerHealth.SetHealth((short)PlayerHealth.MAX_PLAYER_HEALTH, hp, false);
        Mod.Note("Battle.FullHealth", $"{hp} -> {PlayerHealth.MAX_PLAYER_HEALTH}", echo: false);
    }

    // ---- Game events (from ActorPatches) ---------------------------------------------------------

    public void OnPlayerDamaged(int amount, int healthNow)
    {
        if (State != BattleState.Running || Mode == BattleMode.Record) return;

        _score!.PlayerDamaged(amount);
        Mod.Note("Battle.YouWereHit", $"-{amount}, {healthNow} left", echo: false);
        _reshade.Fire("hit", YourHead(), Mathf.Clamp01(amount / 5f), 0.6f);
        if (healthNow <= 0) Finish(survived: false, ChartTime);
    }

    public void OnActorDamaged(Actor actor, int amount)
    {
        if (State != BattleState.Running || !_actors.Contains(actor)) return;
        _score!.ActorDamaged(amount, _phase?.ScoreMultiplier ?? 1);
    }

    private void OnActorKnockedOut(Actor actor)
    {
        if (State != BattleState.Running || !_actors.Contains(actor)) return;
        _score!.ActorKnockedOut(_phase?.ScoreMultiplier ?? 1);
        _reshade.Fire("knockout", actor.Player?.Controller?.transform.position ?? World(0f, 0f, Chart!.Lane.Length), 1f, 1.5f);
    }

    /// <summary>The chart's own clips first, then yours.</summary>
    private Clip? FindClip(string name) =>
        _pendingTake != null && string.Equals(_pendingTake.Name, name, StringComparison.OrdinalIgnoreCase)
            ? _pendingTake
            : _chartClips?.Find(name) ?? Clips?.Find(name);

    private Actor? ActorById(string id)
    {
        Actor? actor = _actors.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));
        if (actor == null || !actor.Exists) Mod.Note("Battle.NoActor", $"\"{id}\" is not in play", echo: false);
        return actor is { Exists: true } ? actor : null;
    }

    private Vector3 World(float x, float y, float z) => Describe.ToUnity(_frame.ToWorld(new System.Numerics.Vector3(x, y, z)));
}
