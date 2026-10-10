using System.Collections;
using BossBattles.Actors;
using BossBattles.Battle;
using BossBattles.Core;
using BossBattles.Flatscreen;
using BossBattles.Services.Audio;
using BossBattles.Services.Charts;
using BossBattles.Services.Clips;
using BossBattles.Services.Scoring;
using BossBattles.UI;
using Il2CppRUMBLE.Managers;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace BossBattles.Testing;

/// <summary>
/// An automatic run of the "Self Test" chart, for checking a build without anyone in the headset.
/// Launch the game with <c>--bossbattles-selftest</c> (or <c>--bossbattles-selftest=ring</c> /
/// <c>=pit</c>); once the Gym is up it plays the chart, judges the run, writes the verdict to
/// <c>UserData/BossBattles/selftest.txt</c> and the log, and quits the game.
/// <para>
/// It checks what a person would otherwise have to watch for: actors came up as simulated players
/// with a body, every projectile spawned and launched, every command ran, the battle finished, and
/// nothing threw - no failed patches, spawns, or commands.
/// </para>
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

    private static readonly string[] FailureCounters =
    {
        "Patches.Disabled",
        "Patches.PatchFailed",
        "Patches.PatchedNothing",
        "Actor.SpawnFailed",
        "Actor.VisualsFailed",
        "Actor.PresentationFailed",
        "Actor.ControllerTypeRestored",
        "Battle.PrepareFailed",
        "Battle.CommandFailed",
        "Battle.ActorsLate",
        "Battle.NoActor",
        "Projectile.NoPool",
        "Projectile.Stalled",
        "Battle.NoClip",
        "Actor.PlacementOff",
        "Arena.NoFloor",
        "Arena.NoMatchSpots",
        "Arena.LoadFailed",
        "Arena.SpotOffFloor",
        "Music.Missing",
        "Music.Failed",
        "Music.Late",
        "Picker.Failed",
        "GameButtons.Failed",
        "Cover.Failed",
        "Effects.Failed",
        "Projectile.OffTheFloor",
        "Battle.VulnerableFailed",
        "Projectile.StateFailed",
        "Battle.CleanupFailed",
        "Actor.UnknownStone",
        "Actor.StoneCatalogFailed",
        "Visuals.Failed",
        "Visuals.FireFailed",
        "Visuals.RestoreFailed",
        "Visuals.PreloadFailed",
        "Battle.MovesNotHeld",
        "Battle.SpawnsNotReturned",
        "Battle.ClearFailed",
        "Battle.MovesNotReleased",
        "Board.Failed",
    };

    private static bool _enabled;
    private static bool _started;
    private static ArenaKind _arena = ArenaKind.Here;
    private static ClipLibrary? _clips;
    private static bool _sawSimulatedActor;
    private static bool _shots;
    private static Action<Vector3, Vector3>? _showCamera;
    private static Func<int, int, byte[]?>? _capture;
    private static ChartPicker? _picker;
    private static string? _pickerProblem;
    private static string? _laneProblem;
    private static string _song = "";

    public static bool Enabled => _enabled;

    public static void Configure(ClipLibrary clips, Action<Vector3, Vector3> showCamera, Func<int, int, byte[]?> capture, ChartPicker picker, Func<string?> newChart)
    {
        _newChart = newChart;
        _picker = picker;
        _showCamera = showCamera;
        _capture = capture;
        _shots = Environment.GetCommandLineArgs().Any(a => string.Equals(a, ShotsFlag, StringComparison.OrdinalIgnoreCase));

        var argument = Environment.GetCommandLineArgs()
            .FirstOrDefault(a => a.StartsWith(Flag, StringComparison.OrdinalIgnoreCase));
        if (argument == null) return;

        _enabled = true;
        _clips = clips;
        if (argument.EndsWith("=ring", StringComparison.OrdinalIgnoreCase)) _arena = ArenaKind.Ring;
        if (argument.EndsWith("=pit", StringComparison.OrdinalIgnoreCase)) _arena = ArenaKind.Pit;
        _recordTest = argument.EndsWith("=record", StringComparison.OrdinalIgnoreCase);
        _lookTest = argument.EndsWith("=look", StringComparison.OrdinalIgnoreCase);
        _measureTest = argument.EndsWith("=measure", StringComparison.OrdinalIgnoreCase);
        var chartAt = argument.IndexOf(ChartPrefix, StringComparison.OrdinalIgnoreCase);
        if (chartAt >= 0) _playbackChart = argument[(chartAt + ChartPrefix.Length)..].Trim('"');

        Mod.Say($"[SelfTest] armed: will play \"{StarterCharts.SelfTestName}\" in {_arena} once the Gym loads{(_shots ? ", with screenshots" : "")}");
    }

    public static void OnSceneLoaded(string scene)
    {
        if (!_enabled || _started || !string.Equals(scene, Arena.GymScene, StringComparison.Ordinal)) return;

        _started = true;
        MelonCoroutines.Start(Run());
    }

    /// <summary>Called each frame: notes whether an actor was ever seen as a simulated player.</summary>
    public static void Update()
    {
        if (_enabled && !_sawSimulatedActor && Mod.Actors.All.Any(a => a.Ready && a.IsSimulated)) _sawSimulatedActor = true;
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
        Mod.Say($"[SelfTest] your look string: {look.Length} chars, \"{look[..Math.Min(look.Length, 120)]}\"");

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
        ChartLoadResult loaded = ChartLoader.Load(StarterCharts.SelfTest);
        Chart? chart = loaded.Chart;
        if (chart == null)
        {
            Finish(loaded.Errors.Select(e => $"the self-test chart does not load: {e}").ToList());
            yield break;
        }

        _clips?.Put(SyntheticClips.Sway(StarterCharts.SelfTestClip, 1.5f));

        // A song beside the chart, as a chart folder would have one: a metronome written here, or
        // a song dropped in by hand to check a format Unity has to decode.
        var assets = Path.Combine(MelonEnvironment.UserDataDirectory, "BossBattles", "SelfTest");
        Directory.CreateDirectory(assets);
        File.WriteAllBytes(Path.Combine(assets, ToneFile), WavFile.Write(WavFile.Metronome(120, chart.End + 2)));
        _song = SongFiles.FirstOrDefault(f => File.Exists(Path.Combine(assets, f))) ?? ToneFile;

        // The Ring is played from a spot the chart names; the Pit is laid out automatically.
        ArenaSpec arena = _arena == ArenaKind.Ring ? new ArenaSpec(ArenaKind.Ring, 0f, -4f, 0f) : new ArenaSpec(_arena);
        chart = chart with { Arena = arena, Music = new MusicSpec(_song, 0, 0.6f) };

        BattleResult? result = null;
        void OnFinished(BattleResult r)
        {
            result = r;
        }
        if (_arena == ArenaKind.Here)
        {
            // The flatscreen spawn faces a wall of scenery; lay the lane where there is room.
            var yours = ActorRig.Of(PlayerManager.instance.localPlayer.Controller);
            (Vector3 direction, var clear) = Arena.OpenestDirection(yours.FloorPoint);
            Mod.Battle.HereFacing = direction;
            Mod.Say($"[SelfTest] laying the lane towards {Describe.Vector(direction)}: {clear:0.0} m clear");
        }

        Mod.Battle.Finished += OnFinished;
        // Something to clear: a battle starts on a clear floor.
        Perform(PlayerManager.instance.localPlayer.Controller, "SpawnCube");
        yield return new WaitForSeconds(1f);

        Mod.Battle.Start(WithLook(chart), null, assets);
        MelonCoroutines.Start(CheckRules());
        if (_arena != ArenaKind.Here) MelonCoroutines.Start(CheckActorHitbox());
        MelonCoroutines.Start(CheckSlides());
        if (_shots) MelonCoroutines.Start(TakeShots());
        MelonCoroutines.Start(CheckYouOnTheLane());

        var deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        while (result == null && Time.realtimeSinceStartup < deadline) yield return null;

        Mod.Battle.Finished -= OnFinished;
        if (result != null) yield return CheckWalls();
        Finish(Judge(chart, result));
    }

    /// <summary>
    /// The picker stands in front of you in the Gym, and a press changes its card - and is undone,
    /// so the chosen chart is left as it was. With shots, a picture of it.
    /// </summary>
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

        if (_shots && _picker.Root is { } card)
        {
            var folder = ShotFolder();
            Vector3 eye = card.position - (card.forward * 1.1f) + (card.right * 0.35f) + (Vector3.up * 0.15f);
            _showCamera?.Invoke(eye, card.position + (Vector3.up * 0.05f));
            yield return null;
            Shoot(folder, "0-picker");
        }

        _picker.Press("prev");
        if (string.IsNullOrWhiteSpace(before) || before == after) _pickerProblem = $"pressing next did not change the picker's card (\"{before}\")";
        Mod.Say($"[SelfTest] picker ({(gameButton ? "the game's NEXT button" : "the mod's own buttons")}): {Flatten(before)} -> {Flatten(after)}");
    }

    /// <summary>
    /// A cover shows on the card: one is drawn here, saved as a PNG beside the self-test's song, and
    /// previewed on the picker in place of the chosen chart's card.
    /// </summary>
    private static IEnumerator CheckCover()
    {
        var path = Path.Combine(MelonEnvironment.UserDataDirectory, "BossBattles", "SelfTest", "selftest-cover.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var image = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++) image.SetPixel(x, y, ((x / 16) + (y / 16)) % 2 == 0 ? new Color(1f, 0.45f, 0.1f) : new Color(0.1f, 0.1f, 0.15f));
        }

        image.Apply();
        File.WriteAllBytes(path, ImageConversion.EncodeToPNG(image));
        UnityEngine.Object.Destroy(image);

        _picker!.Override = new PickerCard("<b>Cover test</b>\n<size=50%>a chart with a cover</size>", path);
        yield return new WaitForSeconds(0.5f);
        if (!_picker.ShowingCover) _pickerProblem = "a chart's cover image did not show on the picker";

        if (_shots && _picker.Root is { } card)
        {
            Vector3 eye = card.position - (card.forward * 1.1f) + (card.right * 0.35f) + (Vector3.up * 0.15f);
            _showCamera?.Invoke(eye, card.position + (Vector3.up * 0.05f));
            yield return null;
            Shoot(ShotFolder(), "0-cover");
        }

        _picker.Override = null;
        yield return new WaitForSeconds(0.4f);
    }

    /// <summary>
    /// The self-test chart allows discs only: once the battle runs, the other spawn moves are off
    /// your list and the disc is not; at the end they are all back.
    /// </summary>
    private static IEnumerator CheckRules()
    {
        var deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        while (Mod.Battle.State != BattleState.Running && Time.realtimeSinceStartup < deadline) yield return null;
        yield return new WaitForSeconds(0.3f);

        // Only in place: loading the Ring or Pit takes the Gym's structures with it anyway.
        if (_arena == ArenaKind.Here && BattleController.LastCleared < 1) _rulesProblem = "the cube spawned before the battle was not cleared off the map";
        if (!Mod.Battle.Withholds("SpawnCube") || Mod.Battle.Withholds("Disc"))
            _rulesProblem = "a discs-only chart did not take the other spawn moves away (and leave the disc)";

        // The gestures themselves - what a VR player actually uses - not just the move list.
        var hadGestures = BattleController.HasGesture("Disc");
        if (!hadGestures) Mod.Say("[SelfTest] rules: no gesture handles on this player (flatscreen?) - gesture check skipped");
        else if (BattleController.HasGesture("SpawnCube") || Mod.Battle.WithheldGesturesLeft > 0)
            _rulesProblem = "a discs-only chart left the cube's spawn gesture working";

        while (Mod.Battle.State == BattleState.Running && Time.realtimeSinceStartup < deadline) yield return null;
        yield return new WaitForSeconds(0.3f);
        if (Mod.Battle.Withholds("SpawnCube")) _rulesProblem = "the spawn moves were not given back after the battle";
        if (hadGestures && !BattleController.HasGesture("SpawnCube")) _rulesProblem = "the cube's spawn gesture was not given back after the battle";
        Mod.Say($"[SelfTest] rules: cleared {BattleController.LastCleared} structure(s), spawns {(_rulesProblem == null ? "withheld and returned" : "wrong")}");
    }

    private static string? _rulesProblem;

    /// <summary>Once the countdown starts you should be standing at the lane's near end.</summary>
    private static IEnumerator CheckYouOnTheLane()
    {
        var deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        while (Mod.Battle.State != BattleState.Countdown && Time.realtimeSinceStartup < deadline) yield return null;
        yield return new WaitForSeconds(0.3f);

        if (Mod.Battle.LaneOrigin is not { } origin || PlayerManager.instance?.localPlayer?.Controller is not { } you) yield break;

        Vector3 feet = ActorRig.Of(you).FloorPoint;
        var off = new Vector2(feet.x - origin.x, feet.z - origin.z).magnitude;
        Mod.Say($"[SelfTest] you are {off:0.00} m from the lane's start {Describe.Vector(origin)}");
        if (off > 0.75f) _laneProblem = $"you were {off:0.0} m from the lane's start - not moved onto it";
    }

    /// <summary>
    /// With the lane still up after the battle, walk into each wall - right, left, back, forward -
    /// with the flatscreen walk stick, dashing into it twice on the way (a dash once went through a
    /// thin wall), and check you are still inside the lane each time. Needs no headset.
    /// </summary>
    private static IEnumerator CheckWalls()
    {
        if (!FlatscreenControls.IsFlatscreen || Mod.Battle.LaneSize is not { } lane) yield break;

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
            Vector3 at = Mod.Battle.ToLane(ActorRig.Of(you).FloorPoint);
            Mod.Say($"[SelfTest] walked and dashed {name}: at lane x {at.x:0.00}, z {at.z:0.00}");
            if (!inside(at)) problems.Add($"walking {name} left the lane: x {at.x:0.0}, z {at.z:0.0}");
        }

        FlatscreenControls.ScriptedWalk = null;
        if (problems.Count > 0) _laneProblem = string.Join("; ", problems);
    }

    private static string Flatten(string text) => System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "").Replace('\n', ' ');

    private static string ShotFolder()
    {
        var folder = Path.Combine(MelonEnvironment.UserDataDirectory, "BossBattles", "SelfTestShots", _arena.ToString());
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// Four screenshots from an overview camera behind you - the lane at the countdown, the first
    /// projectile telegraphing and just launched, the results - into
    /// <c>UserData/BossBattles/SelfTestShots/&lt;arena&gt;</c>. Only with
    /// <c>--bossbattles-shots</c>, so a plain self-test stays quick.
    /// </summary>
    private static IEnumerator TakeShots()
    {
        // One folder per arena, so a Ring run does not overwrite the Gym's pictures.
        var folder = ShotFolder();
        foreach (var old in Directory.GetFiles(folder, "?-*.png").Where(f => !Path.GetFileName(f).StartsWith("0-", StringComparison.Ordinal))) File.Delete(old);

        var deadline = Time.realtimeSinceStartup + TimeoutSeconds;

        while (Mod.Battle.State != BattleState.Countdown && Time.realtimeSinceStartup < deadline) yield return null;
        if (Mod.Battle.Overview is { } view) _showCamera?.Invoke(view.Position, view.LookAt);
        yield return new WaitForSeconds(0.4f);
        Shoot(folder, "1-lane");

        // The first projectile hovering at its origin - the telegraph - is where it is easiest to see.
        while (Mod.Battle.ProjectileCounts.Spawned < 1 && Time.realtimeSinceStartup < deadline) yield return null;
        yield return new WaitForSeconds(0.12f);
        Shoot(folder, "2-telegraph");

        // Mid-flight: at 0.1 s the disc had barely left its origin and looked like the telegraph.
        while (Mod.Battle.ProjectileCounts.Launched < 1 && Time.realtimeSinceStartup < deadline) yield return null;
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
                Mod.Say($"[SelfTest] screenshot {name}: the overview camera is not on");
                return;
            }

            var path = Path.Combine(folder, $"{name}.png");
            File.WriteAllBytes(path, png);
            Mod.Say($"[SelfTest] screenshot {name} -> {path}");
        }
        catch (Exception e)
        {
            Mod.Say($"[SelfTest] screenshot {name} failed: {e}");
        }
    }

    private static List<string> Judge(Chart chart, BattleResult? result)
    {
        var problems = new List<string>();

        if (result == null) problems.Add($"the battle did not finish within {TimeoutSeconds}s (state {Mod.Battle.State})");
        if (Mod.Battle.ReadyActors < chart.Actors.Count) problems.Add($"{Mod.Battle.ReadyActors} of {chart.Actors.Count} actor(s) came up ready");
        if (!_sawSimulatedActor) problems.Add("no actor was ever seen as a ready, simulated player");

        if (Mod.Census["Actor.Clip"] == 0) problems.Add("no actor played the chart's clip");
        if (Mod.Battle.LaneMetres < 2f) problems.Add($"the lane was {Mod.Battle.LaneMetres:0.0} m long");
        if (_pickerProblem != null) problems.Add(_pickerProblem);
        if (Mod.Census["Battle.MovesHeld"] == 0) problems.Add("your moves were not held through the countdown");
        if (_laneProblem != null) problems.Add(_laneProblem);
        if (_rulesProblem != null) problems.Add(_rulesProblem);
        if (_visualsProblem != null) problems.Add(_visualsProblem);
        if (_hitboxProblem != null) problems.Add(_hitboxProblem);
        if (_slideProblem != null) problems.Add(_slideProblem);
        if (Mod.Battle.GroundedLaunched == 0) problems.Add("no grounded projectile was thrown along the floor (the chart's cube is grounded by default)");
        if (Mod.Census["Battle.CleanedUp"] == 0) problems.Add("nothing was cleaned up - the cube that slid past you should have broken after sitting still for 2 s");
        if (Mod.Census["Actor.StoneCatalog"] == 0) problems.Add("the shiftstone catalog was never read - a chart's named stones were not looked up");
        // The second phase turns the edges orange (#ffaa00) - still so on the results.
        if (Mod.Battle.LaneEdgeColour is not { } edge || Mathf.Abs(edge.r - 1f) > 0.02f || Mathf.Abs(edge.g - (170f / 255f)) > 0.02f || edge.b > 0.02f)
            problems.Add($"the second phase's lane colour was not applied (edges {Mod.Battle.LaneEdgeColour})");
        if (Mod.Census["Music.Loaded"] == 0) problems.Add($"the song {_song} never loaded");
        if (Mod.Census["Music.Playing"] == 0) problems.Add("the song never played");
        if (chart.Events.OfType<ProjectileEvent>().Any(p => p.To.AtPlayer) && result is { DamageTaken: 0 })
            problems.Add("a projectile was aimed at you, but the game took no health from you");

        var projectiles = chart.Events.OfType<ProjectileEvent>().Count();
        (var spawned, var launched, var failed) = Mod.Battle.ProjectileCounts;
        if (spawned != projectiles || launched != projectiles || failed != 0)
            problems.Add($"projectiles: {spawned} spawned, {launched} launched, {failed} failed of {projectiles} in the chart");

        foreach (var counter in FailureCounters)
        {
            if (Mod.Census[counter] > 0) problems.Add($"{counter} x{Mod.Census[counter]}");
        }

        return problems;
    }

    private static void Finish(IReadOnlyList<string> problems)
    {
        var verdict = problems.Count == 0
            ? "SELFTEST PASS"
            : $"SELFTEST FAIL{Environment.NewLine}{string.Join(Environment.NewLine, problems.Select(p => $"  - {p}"))}";

        Mod.Say($"[SelfTest] {verdict}");
        Mod.Report("self-test");

        try
        {
            var path = Path.Combine(MelonEnvironment.UserDataDirectory, "BossBattles", "selftest.txt");
            File.WriteAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {_arena}, song {_song}{Environment.NewLine}{verdict}{Environment.NewLine}{Environment.NewLine}census: {Mod.Census.Report()}{Environment.NewLine}");
        }
        catch (IOException e)
        {
            Mod.Say($"[SelfTest] could not write selftest.txt: {e.Message}");
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
