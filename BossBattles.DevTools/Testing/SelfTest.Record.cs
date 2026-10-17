using System.Collections;
using BossBattles.Actors;
using BossBattles.Authoring;
using BossBattles.Battle;
using BossBattles.DevTools.Flatscreen;
using BossBattles.Services.Charts;
using BossBattles.Services.Clips;
using Il2CppRUMBLE.Players;
using MelonLoader.Utils;
using UnityEngine;

namespace BossBattles.DevTools.Testing;

/// <summary>
/// <c>--bossbattles-selftest=record</c>: records, reviews and keeps a clip as a chart maker would,
/// walking and making two moves, then checks the chart and clip on disk.
/// </summary>
internal static partial class SelfTest
{
    private const string RecordChart = /*lang=json*/ """
        {
          // The self-test records into this chart - this comment must survive keeping the clip.
          "format": 1,
          "name": "Record Test",
          // The Ring's floor is flat - the Gym's slopes would let you slide off the spot.
          "arena": { "map": "ring", "at": { "x": 0, "z": -4 }, "facing": 0 },
          "lane": { "length": 8, "width": 3 },
          "actors": [ { "id": "boss", "name": "Recorded", "at": { "x": 0, "z": 8 } } ],
          "end": 3
        }
        """;

    private static bool _recordTest;

    private static IEnumerator RunRecordTest()
    {
        var problems = new List<string>();
        foreach (var old in Directory.GetFiles(ShotFolder(), "*.png").Where(f => Path.GetFileName(f)[0] is >= '5' and <= '9')) File.Delete(old);

        var folder = Path.Combine(MelonEnvironment.UserDataDirectory, "BossBattles", "SelfTest", "record-test");
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, ChartLibrary.FolderChartFile);
        File.WriteAllText(file, RecordChart);

        var entry = new ChartEntry(file, ChartLoader.Load(RecordChart), ChartSource.Folder);
        if (entry.Result.Chart == null)
        {
            Finish(entry.Result.Errors.Select(e => $"the record-test chart does not load: {e}").ToList());
            yield break;
        }

        Mod.Studio.StartRecording(entry, null);

        var deadline = Time.realtimeSinceStartup + 90f;
        while (Mod.Battle.State != BattleState.Running && Time.realtimeSinceStartup < deadline) yield return null;
        yield return new WaitForSeconds(0.5f);

        // The Ring loaded: your player is a new one.
        if (BattlePlayer.Controller is not { } you)
        {
            Finish(["no local player while recording"]);
            yield break;
        }

        if (Mod.Battle.Lane is { } recordLane)
        {
            var at = Mod.Battle.ToLane(ActorRig.From(you).Feet);
            Logger.Info($"[SelfTest] recording from lane x {at.x:0.00}, z {at.z:0.00} (the actor's spot is z 8)");
            if (new Vector2(at.x, at.z - 8f).magnitude > 0.5f) problems.Add($"recording did not start in the actor's place: lane x {at.x:0.0}, z {at.z:0.0}");
            if (_shots) Shoot(ShotFolder(), "5-recording", recordLane, at);
        }

        // The performance: step to one side, spawn a cube and punch it, step back across, jump.
        // (A punch or kick with nothing in front of you never starts - the first run recorded none.)
        yield return Walk(new Vector2(1f, 0f), 0.8f);
        Perform(you, "SpawnCube");
        yield return new WaitForSeconds(0.6f);
        Perform(you, "Straight");
        yield return new WaitForSeconds(0.8f);
        yield return Walk(new Vector2(-1f, 0f), 1.4f);
        Perform(you, "Jump");
        yield return new WaitForSeconds(0.6f);

        // And turn, with the turn stick: the actor must end up facing the same way.
        FlatscreenControls.ScriptedTurn = new Vector2(1f, 0f);
        yield return new WaitForSeconds(0.6f);
        FlatscreenControls.ScriptedTurn = null;
        yield return new WaitForSeconds(0.6f);

        if (!_picker!.InvokeGameButton("stop") && !_picker.Press("stop")) problems.Add("the panel had no STOP button while recording");

        var clip = Mod.Studio.NewClip;
        if (clip == null)
        {
            problems.Add("stopping did not make a clip");
            Finish(problems);
            yield break;
        }

        var xs = clip.Frames.Where(f => f.Offset != null).Select(f => f.Offset!.Value.X).ToArray();
        var span = xs.Length == 0 ? 0f : xs.Max() - xs.Min();
        Logger.Info($"[SelfTest] clip {clip.Name}: {clip.Duration - clip.Start:0.0}s, {clip.Frames.Count} frames, moves {string.Join(", ", clip.Moves.Select(m => $"{m.Move}@{m.Time:0.00}"))}, walked {span:0.00} m across");
        if (clip.Moves.Count < 3) problems.Add($"the clip recorded {clip.Moves.Count} of the 3 moves made");
        if (span < 0.4f) problems.Add($"the clip's path only spans {span:0.00} m across - walking was not recorded");

        var yaws = clip.Frames.Where(f => f.Yaw != null).Select(f => f.Yaw!.Value).ToArray();
        var turned = yaws.Length < 2 ? 0f : Math.Abs(Mathf.DeltaAngle(yaws[0], yaws[^1]));
        Logger.Info($"[SelfTest] the clip turned {turned:0} degrees");
        if (turned < 15f) problems.Add($"the clip's facing only changed {turned:0} degrees - turning was not recorded");

        // The review: the actor performs it at you, running each recorded move.
        var movesBefore = Logged("Actor.Move");
        deadline = Time.realtimeSinceStartup + 60f;
        while (Mod.Studio.State == StudioState.Reviewing && Time.realtimeSinceStartup < deadline)
        {
            if (_shots && Mod.Battle.ChartTime > 1.2 && !File.Exists(Path.Combine(ShotFolder(), "6-review.png")))
            {
                ShowOverview();
                yield return null;
                Shoot(ShotFolder(), "6-review");
            }

            yield return null;
        }

        var actor = Mod.Battle.Actors.Count > 0 ? Mod.Battle.Actors[0] : null;
        if (Mod.Studio.State != StudioState.Deciding) problems.Add($"the review did not end in a decision (studio {Mod.Studio.State})");
        if (actor == null)
        {
            problems.Add("no actor performed the clip");
        }
        else
        {
            var played = Logged("Actor.Move") - movesBefore;
            Logger.Info($"[SelfTest] review: {played} of {clip.Moves.Count} moves played");
            if (played != clip.Moves.Count) problems.Add($"the actor played {played} of the clip's {clip.Moves.Count} moves");
        }

        // The panel has just appeared: its buttons wait a second before they press.
        yield return new WaitForSeconds(1.2f);
        if (!_picker.InvokeGameButton("keep") && !_picker.Press("keep")) problems.Add("the panel had no KEEP button after the review");
        yield return new WaitForSeconds(0.5f);

        CheckKept(file, clip, problems);
        yield return RunSectionTest(file, problems);
        yield return RunSetupTest(file, problems);
        _recordProblems = problems;
    }

    /// <summary>Re-records the middle of the clip: choose it, record it (a spawn and a punch), keep it, check the splice, undo it.</summary>
    private static IEnumerator RunSectionTest(string file, List<string> problems)
    {
        var entry = Reloaded(file);
        if (!Studio.HasTrack(entry))
        {
            problems.Add("after keeping, the chart has no track to re-record");
            yield break;
        }

        Mod.Studio.ChooseRecordingSection(entry, null);
        yield return new WaitForSeconds(0.5f);
        if (!_picker!.ButtonIds.Contains("fromlater")) problems.Add($"choosing a section shows {string.Join(", ", _picker.ButtonIds)}");
        _picker.Press("fromlater");
        yield return new WaitForSeconds(0.4f);
        (var from, var to) = Mod.Studio.Section;
        Logger.Info($"[SelfTest] section {from:0.0}-{to:0.0}s");
        if (_shots && _picker.Root is { } card) ShootPanel(card, "7-section");

        if (!_picker.InvokeGameButton("record") && !_picker.Press("record")) problems.Add("no RECORD button for the section");

        var deadline = Time.realtimeSinceStartup + 60f;
        while ((Mod.Battle.State != BattleState.Running || Mod.Battle.ChartTime < from + 0.2) && Time.realtimeSinceStartup < deadline) yield return null;

        if (BattlePlayer.Controller is { } you)
        {
            var at = Mod.Battle.ToLane(ActorRig.From(you).Feet);
            Logger.Info($"[SelfTest] section recording from lane x {at.x:0.00}, z {at.z:0.00}");
            Perform(you, "SpawnCube");
            yield return new WaitForSeconds(0.5f);
            Perform(you, "Straight");
        }

        deadline = Time.realtimeSinceStartup + 60f;
        while (Mod.Studio.State is StudioState.Recording or StudioState.Reviewing && Time.realtimeSinceStartup < deadline) yield return null;

        var section = Mod.Studio.NewClip;
        if (Mod.Studio.State != StudioState.Deciding || section == null || !Mod.Studio.IsSection)
        {
            problems.Add($"the section did not stop by itself and come up for review (studio {Mod.Studio.State})");
            Mod.Studio.Back();
            yield break;
        }

        Logger.Info($"[SelfTest] section clip {section.Name}: {section.Duration:0.00}s, moves {string.Join(", ", section.Moves.Select(m => $"{m.Move}@{m.Time:0.00}"))}");
        if (section.Moves.Count < 2) problems.Add($"the section recorded {section.Moves.Count} of 2 moves");
        if (section.Start != 0 || section.Duration > to - from + 0.2) problems.Add($"the section clip runs {section.Start:0.0}-{section.Duration:0.0}s, longer than its {to - from:0.0}s section");

        _picker.Press("keep");
        yield return new WaitForSeconds(0.5f);

        var spliced = ChartLoader.Load(File.ReadAllText(file)).Chart;
        var clips = spliced?.Tracks.SingleOrDefault()?.Clips ?? Array.Empty<TrackClip>();
        Logger.Info($"[SelfTest] spliced track: {string.Join(" | ", clips.Select(t => $"{t.Time:0.0}s {t.Clip} from {t.From:0.0}{(t.To is { } x ? $" to {x:0.0}" : "")}"))}");
        if (clips.Count != 3 || clips[1].Clip != section.Name) problems.Add($"keeping the section made {clips.Count} clips, not clip-1 / {section.Name} / clip-1");

        // Undo puts the single clip back.
        entry = Reloaded(file);
        if (!Studio.CanUndo(entry)) problems.Add("no undo after keeping the section");
        Studio.Undo(entry);
        var afterUndo = ChartLoader.Load(File.ReadAllText(file)).Chart?.Tracks.SingleOrDefault()?.Clips.Count ?? 0;
        if (afterUndo != 1) problems.Add($"undo left {afterUndo} clips, not 1");
    }

    /// <summary>Lane setup: two metres longer, half a metre wider, saved - and the rest of the chart left alone.</summary>
    private static IEnumerator RunSetupTest(string file, List<string> problems)
    {
        var entry = Reloaded(file);
        var before = entry.Result.Chart!.Lane;

        Mod.Studio.BeginSetup(entry);
        yield return new WaitForSeconds(1f);
        _picker!.Press("longer");
        _picker.Press("longer");
        _picker.Press("wider");
        yield return new WaitForSeconds(0.6f);

        if (_shots && Mod.Studio.SetupValues.Lane.Length > 0 && BattlePlayer.Controller is { } you)
        {
            var rig = ActorRig.From(you);
            var eye = rig.Feet - (rig.Facing * 2.5f) + (Vector3.up * 3.2f) + (Vector3.Cross(Vector3.up, rig.Facing) * 1.5f);
            _showCamera?.Invoke(eye, rig.Feet + (rig.Facing * 5f));
            yield return null;
            Shoot(ShotFolder(), "8-setup");
        }

        if (!_picker.Press("save")) problems.Add("no SAVE button in lane setup");
        yield return new WaitForSeconds(0.3f);

        var text = File.ReadAllText(file);
        var saved = ChartLoader.Load(text).Chart;
        Logger.Info($"[SelfTest] lane setup saved: {saved?.Lane.Length:0.#} x {saved?.Lane.Width:0.#} m, actor at z {saved?.Actors[0].At.Z:0.#}, {saved?.Tracks.Count} track(s)");
        if (saved == null)
        {
            problems.Add("the chart does not load after lane setup");
        }
        else
        {
            if (saved.Lane != new Lane(before.Length + 2f, before.Width + 0.5f)) problems.Add($"lane setup saved {saved.Lane.Length} x {saved.Lane.Width}, not {before.Length + 2} x {before.Width + 0.5}");
            if (Math.Abs(saved.Actors[0].At.Z - saved.Lane.Length) > 0.01f) problems.Add("the actor at the far end did not move with it");
            if (saved.Tracks.Count != 1) problems.Add("lane setup lost the chart's track");
            if (!text.Contains("this comment must survive", StringComparison.Ordinal)) problems.Add("lane setup lost the chart's comment");
        }
    }

    private static ChartEntry Reloaded(string file) => new(file, ChartLoader.Load(File.ReadAllText(file)), ChartSource.Folder);

    private static void ShootPanel(Transform card, string name)
    {
        var eye = card.position - (card.forward * 1.1f) + (card.right * 0.35f) + (Vector3.up * 0.1f);
        _showCamera?.Invoke(eye, card.position - (Vector3.up * 0.05f));
        Shoot(ShotFolder(), name);
    }

    private static List<string>? _recordProblems;

    private static void CheckKept(string file, Clip clip, List<string> problems)
    {
        var text = File.ReadAllText(file);
        var kept = ChartLoader.Load(text);

        if (!text.Contains("this comment must survive", StringComparison.Ordinal)) problems.Add("keeping the clip lost the chart's comment");
        if (kept.Chart == null)
        {
            problems.Add($"the chart does not load after keeping: {string.Join("; ", kept.Errors)}");
            return;
        }

        var segment = kept.Chart.Tracks.SingleOrDefault()?.Clips.SingleOrDefault();
        if (segment?.Clip != clip.Name) problems.Add($"the chart's track does not play {clip.Name}");
        if (kept.Chart.End < clip.Duration) problems.Add($"the chart ends at {kept.Chart.End:0.0}s, before the clip ({clip.Duration:0.0}s)");

        var clipFile = Path.Combine(Path.GetDirectoryName(file)!, ChartPackage.ClipsFolder, $"{clip.Name}.json");
        (var saved, var error) = File.Exists(clipFile) ? ClipFile.Read(File.ReadAllText(clipFile), clip.Name) : (null, "missing");
        if (saved == null) problems.Add($"the clip's file {clipFile}: {error}");
        else if (saved.Moves.Count != clip.Moves.Count) problems.Add($"the saved clip has {saved.Moves.Count} moves, not {clip.Moves.Count}");

        Logger.Info($"[SelfTest] kept: {Path.GetFileName(clipFile)}, chart format {(text.Contains("\"format\": 2", StringComparison.Ordinal) ? 2 : 1)}, end {kept.Chart.End:0.0}s");
    }

    private static IEnumerator Walk(Vector2 stick, float seconds)
    {
        FlatscreenControls.ScriptedWalk = stick;
        yield return new WaitForSeconds(seconds);
        FlatscreenControls.ScriptedWalk = Vector2.zero;
        yield return new WaitForSeconds(0.2f);
    }

    /// <summary>Runs one of your own moves by name - what the game does when it recognises the gesture.</summary>
    private static void Perform(PlayerController you, string move)
    {
        var stack = you.PlayerProcessor.availableStacks.ToArray().FirstOrDefault(s => string.Equals(s.CachedName, move, StringComparison.OrdinalIgnoreCase));
        if (stack == null)
        {
            Logger.Info($"[SelfTest] you have no move called {move}");
            return;
        }

        you.PlayerProcessor.Execute(stack);
    }

    /// <summary>A screenshot from beside the lane, looking along it at you.</summary>
    private static void Shoot(string folder, string name, Anchor lane, Vector3 at)
    {
        Vector3 World(float x, float y, float z)
        {
            return lane.ToWorld(new System.Numerics.Vector3(x, y, z)).ToUnity();
        }

        _showCamera?.Invoke(World(2.2f, 2.2f, at.z - 3.5f), World(at.x, 1.1f, at.z));
        Shoot(folder, name);
    }
}
