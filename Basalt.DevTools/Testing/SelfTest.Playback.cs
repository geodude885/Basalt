using System.Collections;
using System.Globalization;
using System.Text;
using Basalt.Actors;
using Basalt.Battle;
using Basalt.Services.Charts;
using Basalt.Services.Charts.Model;
using Basalt.Services.Clips;
using Il2CppRUMBLE.MoveSystem;
using MelonLoader.Utils;
using UnityEngine;

namespace Basalt.DevTools.Testing;

/// <summary>
/// <c>chart:&lt;name&gt;</c>: plays one of your charts, invincible, and measures
/// its clips on the actor: moves fired, and structure speeds after slingshots.
/// Per-frame numbers go to <c>SelfTest/playback-&lt;name&gt;.csv</c>.
/// </summary>
internal static partial class SelfTest
{
    /// <summary>A Straight or Uppercut this soon after a Hold counts as slung.</summary>
    private const double SlingWindow = 0.6;

    /// <summary>How long after a move its structure's top speed is looked for.</summary>
    private const double SpeedWindow = 0.4;

    private static string? _playbackChart;

    private static IEnumerator RunPlayback(List<string> problems)
    {
        var library = new ChartLibrary(Path.Combine(MelonEnvironment.UserDataDirectory, "Basalt", "Charts"));
        library.Reload();
        var entry = library.Find(_playbackChart!)
                            ?? library.Entries.FirstOrDefault(e => string.Equals(e.Stem, _playbackChart, StringComparison.OrdinalIgnoreCase));
        if (entry?.Result.Chart is not { } chart)
        {
            problems.Add(entry == null ? $"no chart called \"{_playbackChart}\"" : $"\"{_playbackChart}\" does not load: {string.Join("; ", entry.Result.Errors)}");
            yield break;
        }

        // The recorded moves, in chart time, from the chart's own clips.
        var clips = new ClipLibrary(Path.Combine(entry.AssetFolder, "clips"));
        clips.Reload();
        var recorded = new List<(double Time, string Move)>();
        foreach (var performance in chart.Performances)
        {
            var nextStarts = performance.Clips.Skip(1).Select(t => t.Time).Append(double.MaxValue);
            foreach ((var placed, var until) in performance.Clips.Zip(nextStarts))
            {
                var clip = clips.Find(placed.Clip);
                if (clip == null)
                {
                    problems.Add($"clip \"{placed.Clip}\" is not in {entry.AssetFolder}/clips");
                    continue;
                }

                recorded.AddRange(clip.Moves
                    .Where(move => move.Time >= placed.Start && (placed.End == null || move.Time <= placed.End))
                    .Select(move => (Time: placed.Time + (move.Time - placed.Start), move.Move))
                    .Where(r => r.Time < until && r.Time <= chart.End));
            }
        }

        Logger.Info($"[SelfTest] playing \"{chart.Name}\" ({entry.Stem}): {recorded.Count} recorded move(s) over {chart.End:0.0}s");
        _keepPlayerAlive = true;
        Mod.Battle.Start(new BattleRequest(chart) { AssetFolder = entry.AssetFolder });

        var deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        while (Mod.Battle.Run?.State != BattleState.Running && Time.realtimeSinceStartup < deadline) yield return null;
        if (Mod.Battle.Run?.State != BattleState.Running)
        {
            problems.Add($"the chart never started (state {Mod.Battle.Run?.State.ToString() ?? "no battle"})");
            yield break;
        }

        var shots = Path.Combine(MelonEnvironment.UserDataDirectory, "Basalt", "SelfTestShots", "Playback");
        Directory.CreateDirectory(shots);
        // --basalt-shots-at=120,125.5 picks the chart times; else three spread through it.
        var shotsAt = Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith("--basalt-shots-at=", StringComparison.OrdinalIgnoreCase));
        var times = shotsAt == null
            ? new[] { 2.0, chart.End * 0.35, chart.End * 0.7 }
            : shotsAt[(shotsAt.IndexOf('=') + 1)..].Split(',').Select(t => double.Parse(t, CultureInfo.InvariantCulture)).OrderBy(t => t).ToArray();
        var shotTimes = new Queue<double>(times);
        if (_shots) ShowOverview();

        var samples = new List<(double T, float Speed)>();
        var structureSpeeds = new Dictionary<IntPtr, float>();
        var lowestActor = (Height: float.MaxValue, Id: "", Time: 0.0);
        var highestActor = (Height: float.MinValue, Id: "", Time: 0.0);
        var mostBehind = (Count: 0, Time: 0.0);
        var behindSince = new Dictionary<IntPtr, double>();
        var longestBehind = (Seconds: 0.0, Name: "", Time: 0.0);
        deadline = Time.realtimeSinceStartup + (float)chart.End + 60f;
        while (Mod.Battle.Run?.State == BattleState.Running && Time.realtimeSinceStartup < deadline)
        {
            var t = Mod.Battle.ChartTime;
            var actor = Mod.Actors.Actors.FirstOrDefault(a => a.Ready && a.Body.Clips.Playing);
            if (actor != null) samples.Add((t, FastestStructureOf(actor)));
            foreach (var standing in Mod.Actors.Actors)
            {
                if (!standing.Ready) continue;
                var height = Mod.Battle.ToLane(standing.Body.Rig.Feet).y;
                if (height < lowestActor.Height) lowestActor = (height, standing.Id, t);
                if (t > 0 && height > highestActor.Height) highestActor = (height, standing.Id, t);
            }

            // How many are back there, and how long any one stays: a burst arriving together is fine, a pile is not.
            var behind = StructuresBehindLane();
            if (behind.Count > mostBehind.Count) mostBehind = (behind.Count, t);
            foreach (var gone in behindSince.Keys.Where(k => !behind.ContainsKey(k)).ToList()) behindSince.Remove(gone);
            foreach ((var pointer, var name) in behind)
            {
                if (!behindSince.TryGetValue(pointer, out var since)) behindSince[pointer] = since = t;
                if (t - since > longestBehind.Seconds) longestBehind = (t - since, name, t);
            }

            if (_shots && shotTimes.Count > 0 && t >= shotTimes.Peek())
            {
                shotTimes.Dequeue();
                ShowOverview();
                Shoot(shots, $"{samples.Count:00000}-t{t:0.0}");
            }

            yield return null;
        }

        _keepPlayerAlive = false;
        Logger.Info($"[SelfTest] lowest actor: {lowestActor.Id} at lane y {lowestActor.Height:0.00}, {lowestActor.Time:0.0}s");
        if (lowestActor.Height < -0.3f) problems.Add($"actor {lowestActor.Id} sank {-lowestActor.Height:0.0} m through the floor at {lowestActor.Time:0.0}s");
        Logger.Info($"[SelfTest] highest actor: {highestActor.Id} at lane y {highestActor.Height:0.00}, {highestActor.Time:0.0}s");
        if (highestActor.Height > 0.3f) problems.Add($"actor {highestActor.Id} was lifted {highestActor.Height:0.0} m off the floor at {highestActor.Time:0.0}s - a structure pushing it?");
        var brokenBehind = Lines.Count(l => l.Line.StartsWith("[Battle.CleanedUp]", StringComparison.Ordinal) && l.Line.EndsWith("behind the lane - broken", StringComparison.Ordinal));
        Logger.Info($"[SelfTest] behind the lane: at most {mostBehind.Count} structure(s) at once ({mostBehind.Time:0.0}s), longest stay {longestBehind.Seconds:0.00}s ({longestBehind.Name}); {brokenBehind} broken there (breakBehind {(chart.Rules.BreakBehind ? "on" : "off")})");
        if (chart.Rules.BreakBehind && longestBehind.Seconds > 1.0) problems.Add($"breakBehind is on, but {longestBehind.Name} stayed behind the lane {longestBehind.Seconds:0.0}s (to {longestBehind.Time:0.0}s)");
        Report(entry.Stem, chart, recorded, samples, problems);
    }

    /// <summary>Spawned structures whose middle is past the lane's near end - the pile breakBehind clears.</summary>
    private static Dictionary<IntPtr, string> StructuresBehindLane()
    {
        var behind = new Dictionary<IntPtr, string>();
        var combat = CombatManager.instance;
        if (combat == null) return behind;

        foreach (var s in combat.Structures)
        {
            if (s != null && s.gameObject.activeInHierarchy && Mod.Battle.ToLane(s.transform.position).z < -0.15f) behind[s.Pointer] = s.gameObject.name;
        }

        return behind;
    }

    /// <summary>The fastest structure an actor's move last touched.</summary>
    private static float FastestStructureOf(Actor actor)
    {
        var processor = actor.Ready ? actor.Body.Controller.PlayerProcessor.Pointer : IntPtr.Zero;
        var combat = CombatManager.instance;
        if (processor == IntPtr.Zero || combat == null) return 0f;

        var fastest = 0f;
        foreach (var s in combat.Structures)
        {
            if (s == null || s.Processable?.LastStackConfiguration?.Processor?.Pointer != processor) continue;
            var body = s.GetComponent<Rigidbody>();
            if (body != null && !body.isKinematic) fastest = Math.Max(fastest, body.linearVelocity.magnitude);
        }

        return fastest;
    }

    private static void Report(string stem, Chart chart, List<(double Time, string Move)> recorded, List<(double T, float Speed)> samples, List<string> problems)
    {
        var fired = Logged("Actor.Move");
        if (fired < recorded.Count) problems.Add($"{fired} of {recorded.Count} recorded moves fired");

        // Each Straight / Uppercut: its structure's top speed just after, slung (a Hold just before) or not.
        var slung = new List<float>();
        var plain = new List<float>();
        foreach (((var at, var move), var i) in recorded.Select((r, i) => (r, i)).Where(r => r.r.Move is "Straight" or "Uppercut"))
        {
            var top = samples.Where(s => s.T >= at && s.T <= at + SpeedWindow).Select(s => s.Speed).DefaultIfEmpty(0f).Max();
            var afterHold = recorded.Take(i).Any(r => r.Move.StartsWith("Hold", StringComparison.Ordinal) && at - r.Time <= SlingWindow);
            (afterHold ? slung : plain).Add(top);
        }

        static string Speeds(List<float> v)
        {
            return v.Count == 0 ? "none" : $"{v.Count} hits, median {v.OrderBy(x => x).ElementAt(v.Count / 2):0.0} m/s, top {v.Max():0.0} m/s";
        }

        var summary =
            $"\"{chart.Name}\": {fired} of {recorded.Count} recorded moves fired{Environment.NewLine}" +
            $"Straight/Uppercut after a Hold: {Speeds(slung)}{Environment.NewLine}" +
            $"Straight/Uppercut without: {Speeds(plain)}";
        Logger.Info($"[SelfTest] playback{Environment.NewLine}{summary}");

        try
        {
            var folder = Path.Combine(MelonEnvironment.UserDataDirectory, "Basalt", "SelfTest");
            var csv = new StringBuilder("t,fastest_structure_mps" + Environment.NewLine);
            foreach ((var t, var s) in samples)
                csv.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{t:0.000},{s:0.00}"));
            File.WriteAllText(Path.Combine(folder, $"playback-{stem}.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(folder, $"playback-{stem}.txt"), summary + Environment.NewLine);
        }
        catch (IOException e)
        {
            Logger.Info($"[SelfTest] could not write the playback report: {e.Message}");
        }
    }
}
