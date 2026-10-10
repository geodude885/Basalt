using System.Collections;
using System.Globalization;
using System.Text;
using BossBattles.Actors;
using BossBattles.Battle;
using BossBattles.Core;
using BossBattles.Services.Charts;
using BossBattles.Services.Clips;
using Il2CppRUMBLE.MoveSystem;
using MelonLoader.Utils;
using UnityEngine;

namespace BossBattles.DevTools.Testing;

/// <summary>
/// <c>--bossbattles-selftest=chart:&lt;name&gt;</c>: plays one of your charts, invincible, and measures
/// its takes on the actor: moves fired, path and facing error, and structure speeds after slingshots.
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
        ChartEntry? entry = library.Find(_playbackChart!)
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
        foreach (Track track in chart.Tracks)
        {
            for (var i = 0; i < track.Takes.Count; i++)
            {
                TakeSegment take = track.Takes[i];
                Clip? clip = clips.Find(take.Clip);
                if (clip == null)
                {
                    problems.Add($"take \"{take.Clip}\" is not in {entry.AssetFolder}/clips");
                    continue;
                }

                var until = i + 1 < track.Takes.Count ? track.Takes[i + 1].Time : double.MaxValue;
                foreach (ClipMove move in clip.Moves)
                {
                    var at = take.Time + (move.Time - take.From);
                    if (move.Time >= take.From && (take.To == null || move.Time <= take.To) && at < until && at <= chart.End) recorded.Add((at, move.Move));
                }
            }
        }

        Mod.Say($"[SelfTest] playing \"{chart.Name}\" ({entry.Stem}): {recorded.Count} recorded move(s) over {chart.End:0.0}s");
        Mod.Battle.KeepInvincible = true;
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
        if (_shots && Mod.Battle.Overview is { } view) _showCamera?.Invoke(view.Position, view.LookAt);

        var samples = new List<(double T, float Error, float Speed)>();
        var structureSpeeds = new Dictionary<IntPtr, float>();
        deadline = Time.realtimeSinceStartup + (float)chart.End + 60f;
        while (Mod.Battle.State == BattleState.Running && Time.realtimeSinceStartup < deadline)
        {
            var t = Mod.Battle.ChartTime;
            Actor? actor = Mod.Actors.All.FirstOrDefault(a => a.PlayingTake);
            if (actor != null) samples.Add((t, actor.LastTakePathError, FastestStructureOf(actor)));

            if (_shots && shotTimes.Count > 0 && t >= shotTimes.Peek())
            {
                shotTimes.Dequeue();
                if (Mod.Battle.Overview is { } now) _showCamera?.Invoke(now.Position, now.LookAt);
                Shoot(shots, $"{samples.Count:00000}-t{t:0.0}");
            }

            yield return null;
        }

        Mod.Battle.KeepInvincible = false;
        Report(entry.Stem, chart, recorded, samples, problems);
    }

    /// <summary>The fastest structure an actor's move last touched.</summary>
    private static float FastestStructureOf(Actor actor)
    {
        IntPtr processor = actor.Player?.Controller?.PlayerProcessor?.Pointer ?? IntPtr.Zero;
        CombatManager? combat = CombatManager.instance;
        if (processor == IntPtr.Zero || combat == null) return 0f;

        var fastest = 0f;
        foreach (Structure s in combat.Structures)
        {
            if (s == null || s.Processable?.LastStackConfiguration?.Processor?.Pointer != processor) continue;
            Rigidbody body = s.GetComponent<Rigidbody>();
            if (body != null && !body.isKinematic) fastest = Math.Max(fastest, body.linearVelocity.magnitude);
        }

        return fastest;
    }

    private static void Report(string stem, Chart chart, List<(double Time, string Move)> recorded, List<(double T, float Error, float Speed)> samples, List<string> problems)
    {
        var fired = Mod.Census["Actor.Move"];
        if (fired < recorded.Count) problems.Add($"{fired} of {recorded.Count} recorded moves fired");
        if (Mod.Census["Actor.UnknownMove"] > 0) problems.Add($"Actor.UnknownMove x{Mod.Census["Actor.UnknownMove"]}");

        var following = samples.Where(s => s.T > 0.5).Select(s => s.Error).OrderBy(e => e).ToList();
        var errors = following.Count == 0 ? "no samples"
            : $"median {following[following.Count / 2]:0.000} m, 95% {following[(int)(following.Count * 0.95)]:0.000} m, worst {following[^1]:0.000} m, " +
              $"{following.Count(e => e > 0.15f)} of {following.Count} frames over 0.15 m";

        // Each Straight / Uppercut: its structure's top speed just after, slung (a Hold just before) or not.
        var slung = new List<float>();
        var plain = new List<float>();
        for (var i = 0; i < recorded.Count; i++)
        {
            (var at, var move) = recorded[i];
            if (move is not ("Straight" or "Uppercut")) continue;

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
            $"path error before each correction: {errors}{Environment.NewLine}" +
            $"Straight/Uppercut after a Hold: {Speeds(slung)}{Environment.NewLine}" +
            $"Straight/Uppercut without: {Speeds(plain)}";
        Mod.Say($"[SelfTest] playback{Environment.NewLine}{summary}");

        try
        {
            var folder = Path.Combine(MelonEnvironment.UserDataDirectory, "BossBattles", "SelfTest");
            var csv = new StringBuilder("t,path_error_m,fastest_structure_mps" + Environment.NewLine);
            foreach ((var t, var e, var s) in samples)
                csv.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{t:0.000},{e:0.000},{s:0.00}"));
            File.WriteAllText(Path.Combine(folder, $"playback-{stem}.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(folder, $"playback-{stem}.txt"), summary + Environment.NewLine);
        }
        catch (IOException e)
        {
            Mod.Say($"[SelfTest] could not write the playback report: {e.Message}");
        }
    }
}
