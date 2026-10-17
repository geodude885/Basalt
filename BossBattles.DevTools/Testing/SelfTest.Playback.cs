using System.Collections;
using System.Globalization;
using System.Text;
using BossBattles.Actors;
using BossBattles.Battle;
using BossBattles.Services.Charts;
using BossBattles.Services.Clips;
using Il2CppRUMBLE.MoveSystem;
using MelonLoader.Utils;
using UnityEngine;

namespace BossBattles.DevTools.Testing;

/// <summary>
/// <c>--bossbattles-selftest=chart:&lt;name&gt;</c>: plays one of your charts, invincible, and measures
/// its clips on the actor: moves fired, and structure speeds after slingshots.
/// Per-frame numbers go to <c>SelfTest/playback-&lt;name&gt;.csv</c>.
/// </summary>
internal static partial class SelfTest
{
    private const string ChartPrefix = "=chart:";

    /// <summary>A Straight or Uppercut this soon after a Hold counts as slung.</summary>
    private const double SlingWindow = 0.6;

    /// <summary>How long after a move its structure's top speed is looked for.</summary>
    private const double SpeedWindow = 0.4;

    private static string? _playbackChart;

    private static IEnumerator RunPlayback(List<string> problems)
    {
        var library = new ChartLibrary(Path.Combine(MelonEnvironment.UserDataDirectory, "BossBattles", "Charts"));
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
        foreach (var track in chart.Tracks)
        {
            var nextStarts = track.Clips.Skip(1).Select(t => t.Time).Append(double.MaxValue);
            foreach ((var placed, var until) in track.Clips.Zip(nextStarts))
            {
                var clip = clips.Find(placed.Clip);
                if (clip == null)
                {
                    problems.Add($"clip \"{placed.Clip}\" is not in {entry.AssetFolder}/clips");
                    continue;
                }

                recorded.AddRange(clip.Moves
                    .Where(move => move.Time >= placed.From && (placed.To == null || move.Time <= placed.To))
                    .Select(move => (Time: placed.Time + (move.Time - placed.From), move.Move))
                    .Where(r => r.Time < until && r.Time <= chart.End));
            }
        }

        Logger.Info($"[SelfTest] playing \"{chart.Name}\" ({entry.Stem}): {recorded.Count} recorded move(s) over {chart.End:0.0}s");
        _keepYouAlive = true;
        Mod.Battle.Start(chart, null, entry.AssetFolder);

        var deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        while (Mod.Battle.State != BattleState.Running && Time.realtimeSinceStartup < deadline) yield return null;
        if (Mod.Battle.State != BattleState.Running)
        {
            problems.Add($"the chart never started (state {Mod.Battle.State})");
            yield break;
        }

        var shots = Path.Combine(MelonEnvironment.UserDataDirectory, "BossBattles", "SelfTestShots", "Playback");
        Directory.CreateDirectory(shots);
        // --bossbattles-shots-at=120,125.5 picks the chart times; else three spread through it.
        var shotsAt = Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith("--bossbattles-shots-at=", StringComparison.OrdinalIgnoreCase));
        var times = shotsAt == null
            ? new[] { 2.0, chart.End * 0.35, chart.End * 0.7 }
            : shotsAt[(shotsAt.IndexOf('=') + 1)..].Split(',').Select(t => double.Parse(t, CultureInfo.InvariantCulture)).OrderBy(t => t).ToArray();
        var shotTimes = new Queue<double>(times);
        if (_shots) ShowOverview();

        var samples = new List<(double T, float Speed)>();
        var structureSpeeds = new Dictionary<IntPtr, float>();
        deadline = Time.realtimeSinceStartup + (float)chart.End + 60f;
        while (Mod.Battle.State == BattleState.Running && Time.realtimeSinceStartup < deadline)
        {
            var t = Mod.Battle.ChartTime;
            var actor = Mod.Actors.All.FirstOrDefault(a => a.PlayingClip);
            if (actor != null) samples.Add((t, FastestStructureOf(actor)));

            if (_shots && shotTimes.Count > 0 && t >= shotTimes.Peek())
            {
                shotTimes.Dequeue();
                ShowOverview();
                Shoot(shots, $"{samples.Count:00000}-t{t:0.0}");
            }

            yield return null;
        }

        _keepYouAlive = false;
        Report(entry.Stem, chart, recorded, samples, problems);
    }

    /// <summary>The fastest structure an actor's move last touched.</summary>
    private static float FastestStructureOf(Actor actor)
    {
        var processor = actor.Player?.Controller?.PlayerProcessor?.Pointer ?? IntPtr.Zero;
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
            var folder = Path.Combine(MelonEnvironment.UserDataDirectory, "BossBattles", "SelfTest");
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
