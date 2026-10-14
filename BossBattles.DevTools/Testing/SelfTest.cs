using System.Collections;
using System.Text.RegularExpressions;
using BossBattles.Actors;
using BossBattles.Arenas;
using BossBattles.Battle;
using BossBattles.DevTools.Flatscreen;
using BossBattles.Services.Charts;
using BossBattles.Services.Scoring;
using BossBattles.UI;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace BossBattles.DevTools.Testing;

/// <summary>
/// <c>--bossbattles-selftest[=ring|=pit]</c>: once the Gym is up, plays the self-test chart, judges
/// the run, writes the verdict to <c>UserData/BossBattles/selftest.txt</c> and quits the game.
/// </summary>
internal static partial class SelfTest
{
    private const string Flag = "--bossbattles-selftest";
    private const string ShotsFlag = "--bossbattles-shots";
    private const int ShotWidth = 960;
    private const int ShotHeight = 540;
    private const float GymSettleSeconds = 3f;
    private const float PlayerTimeoutSeconds = 120f;
    private const float TimeoutSeconds = 90f;
    private const float QuitDelaySeconds = 2f;
    private const string ToneFile = "selftest-tone.wav";

    /// <summary>Dropped into the self-test folder by hand, one of these is played instead of the tone - to check a format.</summary>
    private static readonly string[] SongFiles = { "selftest-song.mp3", "selftest-song.ogg", "selftest-song.wav" };

    /// <summary>Every line the mod logged; a run fails on any warning or error.</summary>
    private static readonly List<(LogLevel Level, string Line)> Lines = [];
    private static bool _started;
    private static ArenaKind _arena = ArenaKind.Here;
    private static bool _sawSimulatedActor;
    private static bool _shots;
    private static Action<Vector3, Vector3>? _showCamera;
    private static Func<int, int, byte[]?>? _capture;
    private static ChartPicker? _picker;
    private static string? _pickerProblem;
    private static string? _laneProblem;
    private static string _song = "";

    public static bool Enabled { get; private set; }

    public static void Configure(Action<Vector3, Vector3> showCamera, Func<int, int, byte[]?> capture, ChartPicker picker, Func<string?> newChart)
    {
        _newChart = newChart;
        _picker = picker;
        _showCamera = showCamera;
        _capture = capture;
        _shots = Environment.GetCommandLineArgs().Any(a => string.Equals(a, ShotsFlag, StringComparison.OrdinalIgnoreCase));

        var argument = Environment.GetCommandLineArgs()
            .FirstOrDefault(a => a.StartsWith(Flag, StringComparison.OrdinalIgnoreCase));
        if (argument == null) return;

        Enabled = true;
        Logger.Written += (level, line) => Lines.Add((level, line));
        if (argument.EndsWith("=ring", StringComparison.OrdinalIgnoreCase)) _arena = ArenaKind.Ring;
        if (argument.EndsWith("=pit", StringComparison.OrdinalIgnoreCase)) _arena = ArenaKind.Pit;
        _recordTest = argument.EndsWith("=record", StringComparison.OrdinalIgnoreCase);
        _lookTest = argument.EndsWith("=look", StringComparison.OrdinalIgnoreCase);
        _measureTest = argument.EndsWith("=measure", StringComparison.OrdinalIgnoreCase);
        var chartAt = argument.IndexOf(ChartPrefix, StringComparison.OrdinalIgnoreCase);
        if (chartAt >= 0) _playbackChart = argument[(chartAt + ChartPrefix.Length)..].Trim('"');

        Logger.Info($"[SelfTest] armed: will play \"{SelfTestChart.Name}\" in {_arena} once the Gym loads{(_shots ? ", with screenshots" : "")}");
    }

    public static void OnSceneLoaded(string scene)
    {
        if (!Enabled || _started || !string.Equals(scene, Arena.GymScene, StringComparison.Ordinal)) return;

        _started = true;
        MelonCoroutines.Start(Run());
    }

    /// <summary>Called each frame: notes whether an actor was ever seen as a simulated player.</summary>
    public static void Update()
    {
        if (Enabled && !_sawSimulatedActor && Mod.Actors.All.Any(a => a.Ready && a.Exists)) _sawSimulatedActor = true;
        KeepYouAlive();
    }

    private static IEnumerator Run()
    {
        // The Gym scene loads well before your player exists (a fixed wait was too short in the
        // first run); wait for the player, then let the scene settle.
        var playerDeadline = Time.realtimeSinceStartup + PlayerTimeoutSeconds;
        while (PlayerManager.instance?.localPlayer?.Controller == null && Time.realtimeSinceStartup < playerDeadline) yield return null;

        if (PlayerManager.instance?.localPlayer?.Controller == null)
        {
            Finish(new[] { $"no local player after {PlayerTimeoutSeconds}s in the Gym - was the game launched through Steam?" });
            yield break;
        }

        yield return new WaitForSeconds(GymSettleSeconds);

        // What a chart made in game stores as its boss's look.
        var look = PlayerManager.instance.localPlayer.Data.VisualData.ToPlayfabDataString();
        Logger.Info($"[SelfTest] your look string: {look.Length} chars, \"{look[..Math.Min(look.Length, 120)]}\"");

        if (_measureTest)
        {
            var measureProblems = new List<string>();
            yield return RunMeasure(measureProblems);
            Finish(measureProblems);
            yield break;
        }

        if (_lookTest)
        {
            var lookProblems = new List<string>();
            yield return RunLookTest(lookProblems);
            Finish(lookProblems);
            yield break;
        }

        if (_playbackChart != null)
        {
            var playbackProblems = new List<string>();
            yield return RunPlayback(playbackProblems);
            Finish(playbackProblems);
            yield break;
        }

        if (_recordTest)
        {
            yield return RunRecordTest();
            Finish(_recordProblems ?? ["the record test did not finish"]);
            yield break;
        }

        yield return CheckPicker();

        // The chart built into this build, not the copy in the chart folder (written once, so it
        // can be older than the code under test); its clip is supplied in memory.
        ChartLoadResult loaded = ChartLoader.Load(SelfTestChart.Json);
        Chart? chart = loaded.Chart;
        if (chart == null)
        {
            Finish(loaded.Errors.Select(e => $"the self-test chart does not load: {e}").ToList());
            yield break;
        }


        // A song beside the chart, as a chart folder would have one: a metronome written here, or
        // a song dropped in by hand to check a format Unity has to decode.
        var assets = Path.Combine(MelonEnvironment.UserDataDirectory, "BossBattles", "SelfTest");
        Directory.CreateDirectory(assets);
        File.WriteAllBytes(Path.Combine(assets, ToneFile), TestAudio.Write(TestAudio.Metronome(120, chart.End + 2)));
        _song = SongFiles.FirstOrDefault(f => File.Exists(Path.Combine(assets, f))) ?? ToneFile;

        // In the Ring and Pit, the 10 m lane centred on the arena, running north.
        ArenaSpec arena = _arena is ArenaKind.Ring or ArenaKind.Pit ? new ArenaSpec(_arena, 0f, -5f, 0f) : new ArenaSpec(_arena);
        chart = chart with { Arena = arena, Music = new MusicSpec(_song, 0, 0.6f) };

        BattleResult? result = null;
        void OnFinished(BattleResult r)
        {
            result = r;
        }
        if (_arena == ArenaKind.Here)
        {
            // Somewhere flat and open: you stand at the lane's start, so you are moved there first.
            PlayerController you = PlayerManager.instance.localPlayer.Controller;
            var yours = ActorRig.From(you);
            if (FlatLane(yours.Feet, chart.Lane.Length, chart.Lane.Width) is { } lane)
            {
                you.PlayerMovement.Reposition(lane.Start + (Vector3.up * (yours.Head.position.y - yours.Vr.position.y)), Quaternion.LookRotation(lane.Direction));
                Logger.Info($"[SelfTest] laying the lane at {StringHelpers.Vector(lane.Start)} towards {StringHelpers.Vector(lane.Direction)}");
                yield return new WaitForSeconds(0.5f);
            }
            else
            {
                Logger.Info("[SelfTest] no flat, open spot for the lane near you - laying it where you stand");
            }
        }

        Mod.Battle.Finished += OnFinished;
        // Something to clear: a battle starts on a clear floor.
        Perform(PlayerManager.instance.localPlayer.Controller, "SpawnCube");
        yield return new WaitForSeconds(1f);

        Mod.Battle.Start(WithLook(chart), null, assets, pendingClip: SyntheticClips.Sway(SelfTestChart.Clip, 1.5f));
        MelonCoroutines.Start(CheckRules());
        if (_arena != ArenaKind.Here) MelonCoroutines.Start(CheckActorHitbox());
        MelonCoroutines.Start(CheckSlides());
        if (_shots) MelonCoroutines.Start(TakeShots());
        MelonCoroutines.Start(CheckPlacement());

        var deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        while (result == null && Time.realtimeSinceStartup < deadline) yield return null;

        Mod.Battle.Finished -= OnFinished;
        if (result != null) yield return CheckWalls();
        Finish(Judge(chart, result));
    }

    /// <summary>The picker appears in the Gym and a press changes its card; the press is undone after.</summary>
    private static IEnumerator CheckPicker()
    {
        if (_picker == null)
        {
            _pickerProblem = "no chart picker";
            yield break;
        }

        _picker.Open = true;
        var deadline = Time.realtimeSinceStartup + 5f;
        while (!_picker.Visible && Time.realtimeSinceStartup < deadline) yield return null;

        // Its buttons wait a second after it appears - it can land where a hand is.
        yield return new WaitForSeconds(1.2f);

        if (!_picker.Visible)
        {
            _pickerProblem = "the chart picker never appeared";
            yield break;
        }

        yield return CheckCover();

        var before = _picker.CardText;
        var gameButton = _picker.InvokeGameButton("next");
        if (!gameButton) _picker.Press("next");
        yield return new WaitForSeconds(0.5f);
        var after = _picker.CardText;

        if (_shots && _picker.Root is { } card) ShootPanel(card, "0-picker");

        _picker.Press("prev");
        if (string.IsNullOrWhiteSpace(before) || before == after) _pickerProblem = $"pressing next did not change the picker's card (\"{before}\")";
        Logger.Info($"[SelfTest] picker ({(gameButton ? "the game's NEXT button" : "the mod's own buttons")}): {Regex.Replace(before, "<[^>]+>", "").Replace('\n', ' ')} -> {Regex.Replace(after, "<[^>]+>", "").Replace('\n', ' ')}");
    }

    /// <summary>A cover image drawn here shows on the picker's card.</summary>
    private static IEnumerator CheckCover()
    {
        var path = Path.Combine(MelonEnvironment.UserDataDirectory, "BossBattles", "SelfTest", "selftest-cover.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var image = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        foreach (var pixel in Enumerable.Range(0, 64 * 64))
        {
            (var y, var x) = Math.DivRem(pixel, 64);
            image.SetPixel(x, y, ((x / 16) + (y / 16)) % 2 == 0 ? new Color(1f, 0.45f, 0.1f) : new Color(0.1f, 0.1f, 0.15f));
        }

        image.Apply();
        File.WriteAllBytes(path, ImageConversion.EncodeToPNG(image));
        UnityEngine.Object.Destroy(image);

        _picker!.Override = new PickerCard("<b>Cover test</b>\n<size=50%>a chart with a cover</size>", path);
        yield return new WaitForSeconds(0.5f);
        if (!_picker.ShowingCover) _pickerProblem = "a chart's cover image did not show on the picker";

        if (_shots && _picker.Root is { } card) ShootPanel(card, "0-cover");

        _picker.Override = null;
        yield return new WaitForSeconds(0.4f);
    }

    /// <summary>A discs-only chart takes the other spawn moves and gestures away while running, and gives them back.</summary>
    private static IEnumerator CheckRules()
    {
        var deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        while (Mod.Battle.State != BattleState.Running && Time.realtimeSinceStartup < deadline) yield return null;
        yield return new WaitForSeconds(0.3f);

        // Only in place: loading the Ring or Pit takes the Gym's structures with it anyway.
        if (_arena == ArenaKind.Here && LastCleared < 1) _rulesProblem = "the cube spawned before the battle was not cleared off the map";
        if (Available("SpawnCube") || !Available("Disc"))
            _rulesProblem = $"a discs-only chart did not take the other spawn moves away (and leave the disc): moves {string.Join(", ", You?.PlayerProcessor.availableStacks.ToArray().Select(m => m?.CachedName) ?? [])}";

        // The gestures themselves - what a VR player actually uses - not just the move list.
        var hadGestures = HasGesture("Disc");
        if (!hadGestures) Logger.Info("[SelfTest] rules: no gesture handles on this player (flatscreen?) - gesture check skipped");
        else if (HasGesture("SpawnCube"))
            _rulesProblem = "a discs-only chart left the cube's spawn gesture working";

        while (Mod.Battle.State == BattleState.Running && Time.realtimeSinceStartup < deadline) yield return null;
        yield return new WaitForSeconds(0.3f);
        if (!Available("SpawnCube")) _rulesProblem = "the spawn moves were not given back after the battle";
        if (hadGestures && !HasGesture("SpawnCube")) _rulesProblem = "the cube's spawn gesture was not given back after the battle";
        Logger.Info($"[SelfTest] rules: cleared {LastCleared} structure(s), spawns {(_rulesProblem == null ? "withheld and returned" : "wrong")}");
    }

    private static string? _rulesProblem;

    /// <summary>Once the countdown starts you should be standing at the lane's near end.</summary>
    /// <summary>In the countdown you stand at the lane's start, and each actor on its chart spot, not through the floor.</summary>
    private static IEnumerator CheckPlacement()
    {
        var deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        while (Mod.Battle.State != BattleState.Countdown && Time.realtimeSinceStartup < deadline) yield return null;
        yield return new WaitForSeconds(0.5f);

        if (LaneOrigin is not { } origin || Mod.Battle.Chart is not { } chart || PlayerManager.instance?.localPlayer?.Controller is not { } you) yield break;

        Vector3 feet = ActorRig.From(you).Feet;
        var off = new Vector2(feet.x - origin.x, feet.z - origin.z).magnitude;
        Logger.Info($"[SelfTest] you are {off:0.00} m from the lane's start {StringHelpers.Vector(origin)}");
        if (off > 0.75f) _laneProblem = $"you were {off:0.0} m from the lane's start - not moved onto it";

        foreach (Actor actor in Mod.Battle.Actors.Where(a => a.Exists))
        {
            ActorSpec? spec = chart.Actors.FirstOrDefault(s => s.Id == actor.Id);
            if (spec == null) continue;

            // Across and along the lane only: on uneven ground an actor settles on the real floor.
            Vector3 floor = ActorRig.From(actor.Player!.Controller).Feet;
            Vector3 lane = Mod.Battle.ToLane(floor);
            var away = new Vector2(lane.x - spec.At.X, lane.z - spec.At.Z).magnitude;
            var sunk = origin.y - floor.y;
            Logger.Info($"[SelfTest] {actor} is {away:0.00} m from its spot, {floor.y - origin.y:+0.00;-0.00} m from the lane's height");
            if (away > 0.5f || sunk > 2f) _laneProblem = (_laneProblem == null ? "" : _laneProblem + "; ") + $"{actor} stood {away:0.0} m from its spot and {sunk:0.0} m under the lane";
        }
    }

    /// <summary>After the battle, walking and dashing into each wall must leave you inside the lane.</summary>
    private static IEnumerator CheckWalls()
    {
        if (!FlatscreenControls.IsFlatscreen || Mod.Battle.Chart?.Lane is not { } lane) yield break;

        var problems = new List<string>();
        (string Name, Vector2 Stick, float Seconds, Func<Vector3, bool> Inside)[] walks =
        {
            ("right", new Vector2(1f, 0f), 3f, p => p.x <= (lane.Width / 2f) + 0.9f),
            ("left", new Vector2(-1f, 0f), 5f, p => p.x >= -(lane.Width / 2f) - 0.9f),
            ("back", new Vector2(0f, -1f), 3f, p => p.z >= -1.6f),
            ("forward", new Vector2(0f, 1f), 9f, p => p.z <= (Mod.Battle.Chart?.Zone.Length ?? lane.Length) + 0.9f),
        };

        foreach ((var name, Vector2 stick, var seconds, Func<Vector3, bool> inside) in walks)
        {
            FlatscreenControls.ScriptedWalk = stick;
            yield return new WaitForSeconds(1f);
            if (PlayerManager.instance?.localPlayer?.Controller is { } dasher)
            {
                Perform(dasher, "RockSlide");
                yield return new WaitForSeconds(0.7f);
                Perform(dasher, "RockSlide");
            }

            yield return new WaitForSeconds(Math.Max(0.5f, seconds - 1.7f));
            FlatscreenControls.ScriptedWalk = Vector2.zero;
            yield return new WaitForSeconds(0.3f);

            if (PlayerManager.instance?.localPlayer?.Controller is not { } you) break;
            Vector3 at = Mod.Battle.ToLane(ActorRig.From(you).Feet);
            Logger.Info($"[SelfTest] walked and dashed {name}: at lane x {at.x:0.00}, z {at.z:0.00}");
            if (!inside(at)) problems.Add($"walking {name} left the lane: x {at.x:0.0}, z {at.z:0.0}");
        }

        FlatscreenControls.ScriptedWalk = null;
        if (problems.Count > 0) _laneProblem = string.Join("; ", problems);
    }

    private static string ShotFolder()
    {
        var folder = Path.Combine(MelonEnvironment.UserDataDirectory, "BossBattles", "SelfTestShots", _arena.ToString());
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>With <c>--bossbattles-shots</c>: the lane, a telegraph, a flight and the results, into <c>SelfTestShots/&lt;arena&gt;</c>.</summary>
    private static IEnumerator TakeShots()
    {
        // One folder per arena, so a Ring run does not overwrite the Gym's pictures.
        var folder = ShotFolder();
        foreach (var old in Directory.GetFiles(folder, "?-*.png").Where(f => !Path.GetFileName(f).StartsWith("0-", StringComparison.Ordinal))) File.Delete(old);

        var deadline = Time.realtimeSinceStartup + TimeoutSeconds;

        while (Mod.Battle.State != BattleState.Countdown && Time.realtimeSinceStartup < deadline) yield return null;
        ShowOverview();
        yield return new WaitForSeconds(0.4f);
        Shoot(folder, "1-lane");

        // The first projectile hovering at its origin - the telegraph - is where it is easiest to see.
        while (Logged("Projectile.Spawned") < 1 && Time.realtimeSinceStartup < deadline) yield return null;
        yield return new WaitForSeconds(0.12f);
        Shoot(folder, "2-telegraph");

        // Mid-flight: at 0.1 s the disc had barely left its origin and looked like the telegraph.
        while (Logged("Projectile.Launched") < 1 && Time.realtimeSinceStartup < deadline) yield return null;
        yield return new WaitForSeconds(0.4f);
        Shoot(folder, "3-flight");

        while (Mod.Battle.State != BattleState.Finished && Time.realtimeSinceStartup < deadline) yield return null;
        yield return new WaitForSeconds(0.6f);
        Shoot(folder, "4-results");
    }

    private static void Shoot(string folder, string name)
    {
        try
        {
            var png = _capture?.Invoke(ShotWidth, ShotHeight);
            if (png == null)
            {
                Logger.Info($"[SelfTest] screenshot {name}: the overview camera is not on");
                return;
            }

            var path = Path.Combine(folder, $"{name}.png");
            File.WriteAllBytes(path, png);
            Logger.Info($"[SelfTest] screenshot {name} -> {path}");
        }
        catch (Exception e)
        {
            Logger.Info($"[SelfTest] screenshot {name} failed: {e}");
        }
    }

    private static List<string> Judge(Chart chart, BattleResult? result)
    {
        var problems = new List<string>();

        if (result == null) problems.Add($"the battle did not finish within {TimeoutSeconds}s (state {Mod.Battle.State})");
        var ready = Mod.Battle.Actors.Count(a => a.Ready);
        if (ready < chart.Actors.Count) problems.Add($"{ready} of {chart.Actors.Count} actor(s) came up ready");
        if (!_sawSimulatedActor) problems.Add("no actor was ever seen as a ready, simulated player");

        if (Logged("Actor.Clip") == 0) problems.Add("no actor played the chart's clip");
        if (LaneMetres < 2f) problems.Add($"the lane was {LaneMetres:0.0} m long");
        if (_pickerProblem != null) problems.Add(_pickerProblem);
        if (Logged("Battle.MovesHeld") == 0) problems.Add("your moves were not held through the countdown");
        if (_laneProblem != null) problems.Add(_laneProblem);
        if (_rulesProblem != null) problems.Add(_rulesProblem);
        if (_visualsProblem != null) problems.Add(_visualsProblem);
        if (_hitboxProblem != null) problems.Add(_hitboxProblem);
        if (_slideProblem != null) problems.Add(_slideProblem);
        if (GroundedLaunches == 0) problems.Add("no grounded projectile was thrown along the floor (the chart's cube is grounded by default)");
        if (Logged("Battle.CleanedUp") == 0) problems.Add("nothing was cleaned up - the cube that slid past you should have broken after sitting still for 2 s");
        if (Logged("Actor.StoneCatalog") == 0) problems.Add("the shiftstone catalog was never read - a chart's named stones were not looked up");
        // The second phase turns the edges orange (#ffaa00) - still so on the results.
        if (!LaneShows(new Color(1f, 170f / 255f, 0f))) problems.Add("the second phase's lane colour was not applied");
        if (Logged("Music.Loaded") == 0) problems.Add($"the song {_song} never loaded");
        if (Logged("Music.Playing") == 0) problems.Add("the song never played");
        if (chart.Events.OfType<ProjectileEvent>().Any(p => p.To.AtPlayer) && result is { DamageTaken: 0 })
            problems.Add("a projectile was aimed at you, but the game took no health from you");

        var projectiles = chart.Events.OfType<ProjectileEvent>().Count();
        (var spawned, var launched, var failed) = (Logged("Projectile.Spawned"), Logged("Projectile.Launched"), Logged("Projectile.NoPool"));
        if (spawned != projectiles || launched != projectiles || failed != 0)
            problems.Add($"projectiles: {spawned} spawned, {launched} launched, {failed} failed of {projectiles} in the chart");

        problems.AddRange(Lines
            .Where(l => l.Level >= LogLevel.Warning)
            .GroupBy(l => l.Line.Split(' ')[0])
            .Select(g => $"{g.Key} x{g.Count()}: {g.First().Line}"));

        return problems;
    }

    /// <summary>How many lines carried this tag.</summary>
    private static int Logged(string tag) => Lines.Count(l => l.Line.StartsWith($"[{tag}]", StringComparison.Ordinal));

    private static void Finish(IReadOnlyList<string> problems)
    {
        var verdict = problems.Count == 0
            ? "SELFTEST PASS"
            : $"SELFTEST FAIL{Environment.NewLine}{string.Join(Environment.NewLine, problems.Select(p => $"  - {p}"))}";

        Logger.Info($"[SelfTest] {verdict}");

        try
        {
            var path = Path.Combine(MelonEnvironment.UserDataDirectory, "BossBattles", "selftest.txt");
            File.WriteAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {_arena}, song {_song}{Environment.NewLine}{verdict}{Environment.NewLine}");
        }
        catch (IOException e)
        {
            Logger.Info($"[SelfTest] could not write selftest.txt: {e.Message}");
        }

        MelonCoroutines.Start(QuitSoon());
    }

    private static IEnumerator QuitSoon()
    {
        yield return new WaitForSeconds(QuitDelaySeconds);
        Mod.Battle.Stop("self-test over");
        Application.Quit();
    }
}
