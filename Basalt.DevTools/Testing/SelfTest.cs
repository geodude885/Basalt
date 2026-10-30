using System.Collections;
using System.Text.RegularExpressions;
using Basalt.Arenas;
using Basalt.Battle;
using Basalt.DevTools.Flatscreen;
using Basalt.Menu;
using Basalt.Services.Charts;
using Basalt.Services.Scoring;
using Il2CppRUMBLE.Interactions.InteractionBase;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace Basalt.DevTools.Testing;

/// <summary>
/// The self-tests: named tests run in the game, each judged on its own, with the verdict written to
/// <c>UserData/Basalt/selftest.txt</c>.
/// <list type="bullet">
/// <item><c>--basalt-selftest</c> runs the full set (menu, battle, walls) once the Gym is up, then quits.</item>
/// <item><c>--basalt-selftest=menu,battle,ring</c> runs just those, in order; <c>ring</c> or <c>pit</c> moves the battle there.</item>
/// <item><c>--basalt-selftest-listen</c> keeps the game open: a test list written to <c>SelfTest/request.txt</c> is run
/// and answered in <c>selftest.txt</c>; <c>quit</c> closes the game.</item>
/// <item><c>--basalt-shots</c> adds screenshots, in <c>SelfTestShots/&lt;arena&gt;</c>.</item>
/// </list>
/// </summary>
internal static partial class SelfTest
{
    private const string Flag = "--basalt-selftest";
    private const string ListenFlag = "--basalt-selftest-listen";
    private const string ShotsFlag = "--basalt-shots";
    private const string RequestFile = "request.txt";
    private const string ChartTest = "chart:";
    private const int ShotWidth = 960;
    private const int ShotHeight = 540;
    private const float GymSettleSeconds = 3f;
    private const float PlayerTimeoutSeconds = 120f;
    private const float TimeoutSeconds = 90f;
    private const float QuitDelaySeconds = 2f;
    private const string ToneFile = "selftest-tone.wav";

    /// <summary>Dropped into the self-test folder by hand, one of these is played instead of the tone - to check a format.</summary>
    private static readonly string[] SongFiles = { "selftest-song.mp3", "selftest-song.ogg", "selftest-song.wav" };

    private static readonly Dictionary<string, Func<List<string>, IEnumerator>> Tests = new(StringComparer.OrdinalIgnoreCase)
    {
        // The menu appears, NEXT works through the game's own button, a cover shows, and each difficulty step chooses its chart.
        ["menu"] = RunMenuTest,
        // The self-test chart played and judged: rules, slides, placement, lane colours, music, projectiles, and no warnings.
        ["battle"] = RunBattleTest,
        // After a battle: walking and dashing into each wall and corner leaves you in the lane, on its floor.
        ["walls"] = RunWallsTest,
        // A clip recorded, reviewed and kept, then the lane set up and saved.
        ["record"] = RunRecordTest,
        // One actor recorded, the other recorded with the first as a guide, then both played back together.
        ["duet"] = RunDuetTest,
        // Watch mode plays the chosen chart from the overview camera, with you kept on full health.
        ["watch"] = RunWatchTest,
        // A new chart's boss wears your look and stones, then the chart's own.
        ["look"] = RunLookTest,
        // Each throwable structure's size, for the editor.
        ["measure"] = RunMeasure,
    };

    private static readonly Dictionary<string, string[]> Presets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["full"] = ["menu", "battle", "walls"],
    };

    /// <summary>Every line the mod logged during the current test.</summary>
    private static readonly List<(LogLevel Level, string Line)> Lines = [];
    private static List<string>? _firstRequest;
    private static bool _listening;
    private static bool _running;
    private static bool _settled;

    // The catalog is read once a session, so a later battle in a listening session never logs it again.
    private static bool _stoneCatalogRead;
    private static int _runNumber;
    private static ArenaKind _arena = ArenaKind.Gym;
    private static bool _sawSimulatedActor;
    private static bool _shots;
    private static Action<Vector3, Vector3>? _showCamera;
    private static Func<int, int, byte[]?>? _capture;
    private static DesktopCamera? _desktopCamera;
    private static ChartWatcher? _watcher;
    private static string? _pickerProblem;
    private static string? _laneProblem;
    private static string _song = "";

    public static bool Enabled { get; private set; }

    private static string Folder => Path.Combine(MelonEnvironment.UserDataDirectory, "Basalt", "SelfTest");

    public static void Configure(DesktopCamera camera, ChartWatcher watcher)
    {
        _desktopCamera = camera;
        _watcher = watcher;
        _showCamera = camera.ShowFixed;
        _capture = camera.CapturePng;
        var arguments = Environment.GetCommandLineArgs();
        _shots = arguments.Any(a => string.Equals(a, ShotsFlag, StringComparison.OrdinalIgnoreCase));
        _listening = arguments.Any(a => string.Equals(a, ListenFlag, StringComparison.OrdinalIgnoreCase));

        var argument = arguments.FirstOrDefault(a => a.StartsWith(Flag, StringComparison.OrdinalIgnoreCase) && !string.Equals(a, ListenFlag, StringComparison.OrdinalIgnoreCase));
        if (argument == null && !_listening) return;

        Enabled = true;
        Logger.Written += (level, line) =>
        {
            Lines.Add((level, line));
            _stoneCatalogRead |= line.StartsWith("[Actor.StoneCatalog]", StringComparison.Ordinal);
        };
        if (argument != null) _firstRequest = [argument.Contains('=') ? argument[(argument.IndexOf('=') + 1)..] : ""];
        Directory.CreateDirectory(Folder);
        File.Delete(Path.Combine(Folder, RequestFile));
        Logger.Info($"[SelfTest] armed{(_firstRequest != null ? $": \"{_firstRequest[0]}\" once the Gym loads" : "")}{(_listening ? $", then listening for {RequestFile}" : "")}{(_shots ? ", with screenshots" : "")}");
    }

    public static void OnSceneLoaded(string scene)
    {
        if (!Enabled || _runNumber > 0 || _running || !string.Equals(scene, Maps.GymScene, StringComparison.Ordinal)) return;

        if (_firstRequest is [var request]) MelonCoroutines.Start(Run(request));
    }

    /// <summary>Called each frame: notes whether an actor was ever seen as a simulated player, and picks up requests.</summary>
    public static void Update()
    {
        if (!Enabled) return;
        if (!_sawSimulatedActor && Mod.Actors.Actors.Any(a => a.Ready)) _sawSimulatedActor = true;
        KeepPlayerAlive();

        if (!_listening || _running || Time.frameCount % 30 != 0) return;
        var path = Path.Combine(Folder, RequestFile);
        if (!File.Exists(path)) return;

        var request = File.ReadAllText(path).Trim();
        File.Delete(path);
        if (string.Equals(request, "quit", StringComparison.OrdinalIgnoreCase)) Application.Quit();
        else MelonCoroutines.Start(Run(request));
    }

    // A comma-separated list of tests and presets, ring or pit, and chart:<name>; empty is the full set.
    private static List<string> Parse(string request, out ArenaKind arena)
    {
        var parts = request.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(p => p.Trim('"')).ToList();
        arena = parts.Any(p => p.Equals("ring", StringComparison.OrdinalIgnoreCase)) ? ArenaKind.Ring
            : parts.Any(p => p.Equals("pit", StringComparison.OrdinalIgnoreCase)) ? ArenaKind.Pit
            : ArenaKind.Gym;

        var tests = parts.Where(p => !p.Equals("ring", StringComparison.OrdinalIgnoreCase) && !p.Equals("pit", StringComparison.OrdinalIgnoreCase)).ToList();
        if (tests.Count == 0) tests = ["full"];
        return tests.SelectMany(p => Presets.TryGetValue(p, out var preset) ? preset : [p]).ToList();
    }

    private static IEnumerator Run(string request)
    {
        _running = true;
        _runNumber++;
        var tests = Parse(request, out _arena);
        Logger.Info($"[SelfTest] run {_runNumber}: {string.Join(", ", tests)} in {_arena}");

        var report = new List<string>();
        var failed = false;
        foreach (var test in tests)
        {
            var problems = new List<string>();
            var started = Time.realtimeSinceStartup;
            yield return Prepare(test, problems);

            if (problems.Count > 0) { }
            else if (test.StartsWith(ChartTest, StringComparison.OrdinalIgnoreCase))
            {
                _playbackChart = test[ChartTest.Length..];
                yield return RunPlayback(problems);
            }
            else if (Tests.TryGetValue(test, out var run))
            {
                yield return run(problems);
            }
            else
            {
                problems.Add($"no test called \"{test}\" - there are {string.Join(", ", Tests.Keys.Concat(Presets.Keys))} and chart:<name>");
            }

            failed |= problems.Count > 0;
            report.Add($"{(problems.Count == 0 ? "PASS" : "FAIL")} {test} ({Time.realtimeSinceStartup - started:0.0}s)");
            report.AddRange(problems.Select(p => $"  - {p}"));
            Logger.Info($"[SelfTest] {report[^(problems.Count + 1)]}");
        }

        Finish(tests, report, failed);
        _running = false;
    }

    // Each test starts in the Gym with your player up, except walls, which carries on from the battle before it.
    private static IEnumerator Prepare(string test, List<string> problems)
    {
        Lines.Clear();
        _pickerProblem = _laneProblem = _rulesProblem = _visualsProblem = _hitboxProblem = _slideProblem = null;
        _sawSimulatedActor = false;
        if (test.Equals("walls", StringComparison.OrdinalIgnoreCase)) yield break;

        if (!ArenaKind.Gym.IsLoaded() || Mod.Battle.Run != null)
        {
            Mod.Battle.Stop("self-test: next test");
            if (!ArenaKind.Gym.IsLoaded()) Mod.Battle.ReturnToGym();
        }

        var deadline = Time.realtimeSinceStartup + PlayerTimeoutSeconds;
        while ((!ArenaKind.Gym.IsLoaded() || BattlePlayer.Player?.Controller == null) && Time.realtimeSinceStartup < deadline) yield return null;
        if (BattlePlayer.Player?.Controller == null)
        {
            problems.Add($"no local player after {PlayerTimeoutSeconds}s in the Gym - was the game launched through Steam?");
            yield break;
        }

        // The Gym scene loads well before it settles, the first time.
        if (!_settled)
        {
            _settled = true;
            yield return new WaitForSeconds(GymSettleSeconds);
            var look = BattlePlayer.Player.Data.VisualData.ToPlayfabDataString();
            Logger.Info($"[SelfTest] your look string: {look.Length} chars, \"{look[..Math.Min(look.Length, 120)]}\"");
        }

        Lines.Clear();
    }

    private static IEnumerator RunMenuTest(List<string> problems)
    {
        yield return CheckPicker();
        if (_pickerProblem != null) problems.Add(_pickerProblem);
    }

    private static IEnumerator RunWallsTest(List<string> problems)
    {
        if (Mod.Battle.Run?.Lane == null)
        {
            problems.Add("walls needs a battle just before it, still laid out - run battle,walls");
            yield break;
        }

        yield return CheckWalls();
        if (_laneProblem != null) problems.Add(_laneProblem);
    }

    private static IEnumerator RunBattleTest(List<string> problems)
    {
        if (BattlePlayer.Player?.Controller is not { } playerController)
        {
            problems.Add("no local player to battle with");
            yield break;
        }

        // The chart built into this build, not the copy in the chart folder (written once, so it
        // can be older than the code under test); its clip is supplied in memory.
        var loaded = ChartLoader.Load(SelfTestChart.Json);
        var chart = loaded.Chart;
        if (chart == null)
        {
            problems.AddRange(loaded.Errors.Select(e => $"the self-test chart does not load: {e}"));
            yield break;
        }


        // A song beside the chart, as a chart folder would have one: a metronome written here, or
        // a song dropped in by hand to check a format Unity has to decode.
        var assets = Folder;
        Directory.CreateDirectory(assets);
        File.WriteAllBytes(Path.Combine(assets, ToneFile), TestAudio.Write(TestAudio.Metronome(120, chart.End + 2)));
        _song = SongFiles.FirstOrDefault(f => File.Exists(Path.Combine(assets, f))) ?? ToneFile;

        // In the Ring and Pit, the 10 m lane centred on the arena, running north.
        var arena = _arena is ArenaKind.Ring or ArenaKind.Pit ? new ArenaSpec(_arena, 0f, -5f, 0f) : new ArenaSpec(_arena);
        chart = chart with { Arena = arena, Music = new MusicSpec(_song, 0, 0.6f) };

        BattleResult? result = null;
        void OnFinished(BattleResult r)
        {
            result = r;
        }
        // In the Gym, its own lane somewhere flat and open near you rather than at the Gym's origin.
        Anchor? gymLane = null;
        if (_arena == ArenaKind.Gym)
        {
            var playerRig = PlayerRig.From(playerController);
            if (FlatLane(playerRig.Feet, chart.Lane.Length, chart.Lane.Width) is { } lane)
            {
                gymLane = Anchor.Facing(lane.Start.ToNumerics(), lane.Direction.ToNumerics());
                Logger.Info($"[SelfTest] laying the lane at {lane.Start.Describe()} towards {lane.Direction.Describe()}");
            }
            else
            {
                gymLane = Anchor.Facing(playerRig.Feet.ToNumerics(), playerRig.Facing.ToNumerics());
                Logger.Info("[SelfTest] no flat, open spot for the lane near you - laying it where you stand");
            }
        }

        Mod.Battle.Finished += OnFinished;
        // Something to clear: a battle starts on a clear floor.
        Perform(playerController, "SpawnCube");
        yield return new WaitForSeconds(1f);

        Mod.Battle.Start(new BattleRequest(WithLook(chart)) { AssetFolder = assets, PendingClip = SyntheticClips.Sway(SelfTestChart.Clip, 1.5f), Lane = gymLane });
        MelonCoroutines.Start(CheckRules());
        if (_arena != ArenaKind.Gym) MelonCoroutines.Start(CheckActorHitbox());
        MelonCoroutines.Start(CheckSlides());
        if (_shots) MelonCoroutines.Start(TakeShots());
        MelonCoroutines.Start(CheckPlacement());

        var deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        while (result == null && Time.realtimeSinceStartup < deadline) yield return null;

        Mod.Battle.Finished -= OnFinished;
        problems.AddRange(Judge(chart, result));
    }

    /// <summary>The picker appears in the Gym and a press changes its card; the press is undone after.</summary>
    private static IEnumerator CheckPicker()
    {
        var deadline = Time.realtimeSinceStartup + 5f;
        while (Mod.Menu.Root?.activeSelf != true && Time.realtimeSinceStartup < deadline) yield return null;

        // Its buttons wait a second after it appears - it can land where a hand is.
        yield return new WaitForSeconds(1.2f);

        if (Mod.Menu.Root?.activeSelf != true)
        {
            _pickerProblem = "the chart picker never appeared";
            yield break;
        }

        yield return CheckCover();

        var before = CardText;
        var pushed = PushGameButton("next");
        yield return new WaitForSeconds(0.5f);
        var after = CardText;

        if (_shots && Mod.Menu.Root is { } card) ShootPanel(card.transform, "0-picker");

        Mod.Menu.ButtonRow?.Press("prev");
        if (string.IsNullOrWhiteSpace(before) || before == after) _pickerProblem = $"pressing next did not change the picker's card (\"{before}\")";
        if (!pushed) _pickerProblem = "the menu had no game NEXT button to push";
        Logger.Info($"[SelfTest] menu: {Regex.Replace(before, "<[^>]+>", "").Replace('\n', ' ')} -> {Regex.Replace(after, "<[^>]+>", "").Replace('\n', ' ')}");

        yield return CheckDifficulties();
    }

    /// <summary>For each song with more than one chart, each step of its difficulty slider chooses that chart; the first is chosen again after.</summary>
    private static IEnumerator CheckDifficulties()
    {
        var songsTried = 0;
        foreach (var _ in Mod.Charts.Library.Songs)
        {
            yield return new WaitForSeconds(0.4f);
            if (Mod.Menu.StepSlider?.Spec?.Labels.Count > 1 && Mod.Charts.CurrentlyChosen is { } startedOn)
            {
                songsTried++;
                var difficulties = Mod.Charts.Library.DifficultiesOf(startedOn);
                foreach (var step in Enumerable.Range(0, difficulties.Count))
                {
                    Mod.Menu.StepSlider.Root?.GetComponentInChildren<InteractionSlider>(true)?.SetStep(step, false, false);
                    yield return new WaitForSeconds(0.4f);
                    if (Mod.Charts.CurrentlyChosen != difficulties[step]) _pickerProblem = $"step {step} of {startedOn.Name}'s slider chose {Mod.Charts.CurrentlyChosen?.Stem}, not {difficulties[step].Stem}";
                }

                if (_shots && Mod.Menu.Root is { } card) ShootPanel(card.transform, "0-difficulties");
                Mod.Charts.ChooseDifficulty(startedOn);
                Logger.Info($"[SelfTest] difficulties of {startedOn.Name}: {string.Join(", ", difficulties.Select(d => d.Stem))}");
            }

            Mod.Charts.StepSong(1);
        }

        Logger.Info($"[SelfTest] songs with difficulties: {songsTried}");
    }

    /// <summary>A cover image drawn here shows on the picker's card.</summary>
    private static IEnumerator CheckCover()
    {
        var path = Path.Combine(MelonEnvironment.UserDataDirectory, "Basalt", "SelfTest", "selftest-cover.png");
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

        // Shown and judged in one frame: the menu's next refresh puts the screen's own card back.
        Mod.Menu.SelectedChartDescription?.Show(new MenuCard("Cover test<\n<size=50%>a chart with a cover</size>", path));
        if (Mod.Menu.SelectedChartDescription?.Cover.activeSelf != true) _pickerProblem = "a chart's cover image did not show on the picker";

        if (_shots && Mod.Menu.Root is { } card) ShootPanel(card.transform, "0-cover");

        yield return new WaitForSeconds(0.4f);
    }

    /// <summary>A discs-only chart takes the other spawn moves and gestures away while running, and gives them back.</summary>
    private static IEnumerator CheckRules()
    {
        var deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        while (Mod.Battle.Run?.State != BattleState.Running && Time.realtimeSinceStartup < deadline) yield return null;
        yield return new WaitForSeconds(0.3f);

        // Only in place: loading the Ring or Pit takes the Gym's structures with it anyway.
        if (_arena == ArenaKind.Gym && LastCleared < 1) _rulesProblem = "the cube spawned before the battle was not cleared off the map";
        if (Available("SpawnCube") || !Available("Disc"))
            _rulesProblem = $"a discs-only chart did not take the other spawn moves away (and leave the disc): moves {string.Join(", ", You?.PlayerProcessor.availableStacks.ToArray().Select(m => m?.CachedName) ?? [])}";

        // The gestures themselves - what a VR player actually uses - not just the move list.
        var hadGestures = HasGesture("Disc");
        if (!hadGestures) Logger.Info("[SelfTest] rules: no gesture handles on this player (flatscreen?) - gesture check skipped");
        else if (HasGesture("SpawnCube"))
            _rulesProblem = "a discs-only chart left the cube's spawn gesture working";

        while (Mod.Battle.Run?.State == BattleState.Running && Time.realtimeSinceStartup < deadline) yield return null;
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
        while (Mod.Battle.Run?.State != BattleState.Countdown && Time.realtimeSinceStartup < deadline) yield return null;
        yield return new WaitForSeconds(0.5f);

        if (LaneOrigin is not { } origin || Mod.Battle.Run?.Request.Chart is not { } chart || BattlePlayer.Player?.Controller is not { } playerController) yield break;

        var feet = PlayerRig.From(playerController).Feet;
        var off = new Vector2(feet.x - origin.x, feet.z - origin.z).magnitude;
        Logger.Info($"[SelfTest] you are {off:0.00} m from the lane's start {origin.Describe()}");
        if (off > 0.75f) _laneProblem = $"you were {off:0.0} m from the lane's start - not moved onto it";

        foreach (var actor in Mod.Actors.Actors)
        {
            if (!actor.Ready) continue;
            var spec = chart.Actors.FirstOrDefault(s => s.Id == actor.Id);
            if (spec == null) continue;

            // Across and along the lane only: on uneven ground an actor settles on the real floor.
            var floor = actor.Body.Rig.Feet;
            var lane = Mod.Battle.ToLane(floor);
            var away = new Vector2(lane.x - spec.At.X, lane.z - spec.At.Z).magnitude;
            var sunk = origin.y - floor.y;
            Logger.Info($"[SelfTest] {actor} is {away:0.00} m from its spot, {floor.y - origin.y:+0.00;-0.00} m from the lane's height");
            if (away > 0.5f || sunk > 2f) _laneProblem = (_laneProblem == null ? "" : _laneProblem + "; ") + $"{actor} stood {away:0.0} m from its spot and {sunk:0.0} m under the lane";
        }
    }

    /// <summary>After the battle, walking and dashing into each wall and the zone's front corners must leave you inside the lane, on its floor.</summary>
    private static IEnumerator CheckWalls()
    {
        if (!FlatscreenControls.IsFlatscreen || Mod.Battle.Run?.Request.Chart?.Lane is not { } lane) yield break;

        var problems = new List<string>();
        (string Name, Vector2 Stick, float Seconds, Func<Vector3, bool> Inside)[] walks =
        {
            ("right", new Vector2(1f, 0f), 3f, p => p.x <= (lane.Width / 2f) + 0.9f),
            ("left", new Vector2(-1f, 0f), 5f, p => p.x >= -(lane.Width / 2f) - 0.9f),
            ("back", new Vector2(0f, -1f), 3f, p => p.z >= -1.6f),
            ("forward", new Vector2(0f, 1f), 9f, p => p.z <= (Mod.Battle.Run?.Request.Chart?.Zone.Length ?? lane.Length) + 0.9f),
            ("forward left", new Vector2(-0.7f, 0.7f), 5f, p => p.x >= -(lane.Width / 2f) - 0.9f),
            ("forward right", new Vector2(0.7f, 0.7f), 5f, p => p.x <= (lane.Width / 2f) + 0.9f),
        };

        foreach ((var name, var stick, var seconds, var inside) in walks)
        {
            FlatscreenControls.ScriptedWalk = stick;
            yield return new WaitForSeconds(1f);
            if (BattlePlayer.Player?.Controller is { } dasher)
            {
                Perform(dasher, "RockSlide");
                yield return new WaitForSeconds(0.7f);
                Perform(dasher, "RockSlide");
            }

            yield return new WaitForSeconds(Math.Max(0.5f, seconds - 1.7f));
            FlatscreenControls.ScriptedWalk = Vector2.zero;
            yield return new WaitForSeconds(0.3f);

            if (BattlePlayer.Player?.Controller is not { } playerController) break;
            var at = Mod.Battle.ToLane(PlayerRig.From(playerController).Feet);
            Logger.Info($"[SelfTest] walked and dashed {name}: at lane x {at.x:0.00}, y {at.y:0.00}, z {at.z:0.00}");
            if (!inside(at)) problems.Add($"walking {name} left the lane: x {at.x:0.0}, z {at.z:0.0}");
            if (at.y < -0.3f) problems.Add($"walking {name} sank {-at.y:0.0} m through the floor");
        }

        FlatscreenControls.ScriptedWalk = null;
        if (problems.Count > 0) _laneProblem = string.Join("; ", problems);
    }

    private static string ShotFolder()
    {
        var folder = Path.Combine(MelonEnvironment.UserDataDirectory, "Basalt", "SelfTestShots", _arena.ToString());
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>With <c>--basalt-shots</c>: the lane, a telegraph, a flight and the results, into <c>SelfTestShots/&lt;arena&gt;</c>.</summary>
    private static IEnumerator TakeShots()
    {
        // One folder per arena, so a Ring run does not overwrite the Gym's pictures.
        var folder = ShotFolder();
        foreach (var old in Directory.GetFiles(folder, "?-*.png").Where(f => !Path.GetFileName(f).StartsWith("0-", StringComparison.Ordinal))) File.Delete(old);

        var deadline = Time.realtimeSinceStartup + TimeoutSeconds;

        while (Mod.Battle.Run?.State != BattleState.Countdown && Time.realtimeSinceStartup < deadline) yield return null;
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

        while (Mod.Battle.Run?.State != BattleState.Finished && Time.realtimeSinceStartup < deadline) yield return null;
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

        if (result == null) problems.Add($"the battle did not finish within {TimeoutSeconds}s (state {Mod.Battle.Run?.State.ToString() ?? "no battle"})");
        var ready = Mod.Actors.Actors.Count(a => a.Ready);
        if (ready < chart.Actors.Count) problems.Add($"{ready} of {chart.Actors.Count} actor(s) came up ready");
        if (!_sawSimulatedActor) problems.Add("no actor was ever seen as a ready, simulated player");

        if (Logged("Actor.Clip") == 0) problems.Add("no actor played the chart's clip");
        if (LaneMetres < 2f) problems.Add($"the lane was {LaneMetres:0.0} m long");
        if (Logged("Battle.MovesHeld") == 0) problems.Add("your moves were not held through the countdown");
        if (_laneProblem != null) problems.Add(_laneProblem);
        if (_rulesProblem != null) problems.Add(_rulesProblem);
        if (_visualsProblem != null) problems.Add(_visualsProblem);
        if (_hitboxProblem != null) problems.Add(_hitboxProblem);
        if (_slideProblem != null) problems.Add(_slideProblem);
        if (GroundedLaunches == 0) problems.Add("no grounded projectile was thrown along the floor (the chart's cube is grounded by default)");
        if (Logged("Battle.CleanedUp") == 0) problems.Add("nothing was cleaned up - the cube that slid past you should have broken after sitting still for 2 s");
        if (!_stoneCatalogRead) problems.Add("the shiftstone catalog was never read - a chart's named stones were not looked up");
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
    private static string CardText => Mod.Menu.SelectedChartDescription?.Text.text ?? "";

    // As a hand pushing it would: through the game button's own onPressed, so its wiring is checked too.
    private static bool PushGameButton(string id)
    {
        var button = Mod.Menu.ButtonRow?.Buttons.FirstOrDefault(b => b.Spec.Id == id);
        if (button?.Root.GetComponentInChildren<InteractionButton>(true) is not { } interaction) return false;

        interaction.onPressed.Invoke();
        return true;
    }

    private static int Logged(string tag) => Lines.Count(l => l.Line.StartsWith($"[{tag}]", StringComparison.Ordinal));

    private static void Finish(IReadOnlyList<string> tests, List<string> report, bool failed)
    {
        var verdict = failed ? "SELFTEST FAIL" : "SELFTEST PASS";
        Logger.Info($"[SelfTest] {verdict}");

        try
        {
            File.WriteAllText(
                Path.Combine(MelonEnvironment.UserDataDirectory, "Basalt", "selftest.txt"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} run {_runNumber}: {string.Join(", ", tests)} in {_arena}, song {_song}{Environment.NewLine}" +
                string.Join(Environment.NewLine, report) + Environment.NewLine + verdict + Environment.NewLine);
        }
        catch (IOException e)
        {
            Logger.Info($"[SelfTest] could not write selftest.txt: {e.Message}");
        }

        if (!_listening) MelonCoroutines.Start(QuitSoon());
    }

    private static IEnumerator QuitSoon()
    {
        yield return new WaitForSeconds(QuitDelaySeconds);
        Mod.Battle.Stop("self-test over");
        Application.Quit();
    }
}
