using System.Collections;
using BossBattles.Actors;
using BossBattles.Battle;
using BossBattles.Services.Charts;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;
using MelonLoader.Utils;
using UnityEngine;

namespace BossBattles.DevTools.Testing;

/// <summary>
/// <c>--bossbattles-selftest=look</c>: a new chart's boss stores and wears your look and stones, and
/// wears a changed look after editing one part. Close-ups go to <c>SelfTestShots/Look</c>.
/// </summary>
internal static partial class SelfTest
{
    private static bool _lookTest;
    private static Func<string?>? _newChart;

    private static IEnumerator RunLookTest(List<string> problems)
    {
        Player you = PlayerManager.instance.localPlayer;
        var yours = you.Data.VisualData.ToPlayfabDataString();
        var plain = PlayerVisualData.Default.ToPlayfabDataString();
        List<string> stones = Shiftstones.EquippedBy(you.Controller);
        Logger.Info($"[SelfTest] look: yours \"{yours}\"{Environment.NewLine}default \"{plain}\"{Environment.NewLine}your stones: {string.Join(", ", stones)}");

        // 1. A chart made in game.
        var folder = _newChart?.Invoke();
        if (folder == null)
        {
            problems.Add("NEW CHART made no chart");
            yield break;
        }

        var file = Path.Combine(folder, ChartLibrary.FolderChartFile);
        Chart? made = ChartLoader.Load(File.ReadAllText(file)).Chart;
        ActorSpec? boss = made?.Actors.Count > 0 ? made.Actors[0] : null;
        if (boss == null)
        {
            problems.Add("the new chart does not load");
        }
        else
        {
            if (boss.Look != yours) problems.Add($"the new chart's boss look is \"{boss.Look}\", not yours");
            if (boss.Shiftstones == null || !boss.Shiftstones.SequenceEqual(stones)) problems.Add($"the new chart's boss stones are [{string.Join(", ", boss.Shiftstones ?? Array.Empty<string>())}], not yours [{string.Join(", ", stones)}]");
        }

        if (made != null) yield return PlayAndShoot(made, folder, yours, "1-made", problems);

        // 2. One part of the look changed, as someone editing the chart would.
        var edited = EditOnePart(yours, plain, out var change);
        Logger.Info($"[SelfTest] look: changed {change}");
        if (edited == yours) problems.Add("could not find a part of your look to change");

        var text = File.ReadAllText(file);
        File.WriteAllText(file, text.Replace(yours, edited, StringComparison.Ordinal));
        Chart? changed = ChartLoader.Load(File.ReadAllText(file)).Chart;
        if (changed?.Actors[0].Look != edited) problems.Add("the edited look was not read back from the chart");
        else yield return PlayAndShoot(changed, folder, edited, "2-edited", problems);

        // 3. The default character entirely - a look nothing like yours, to see one can change at all.
        File.WriteAllText(file, File.ReadAllText(file).Replace(edited, plain, StringComparison.Ordinal));
        Chart? plainChart = ChartLoader.Load(File.ReadAllText(file)).Chart;
        if (plainChart != null) yield return PlayAndShoot(plainChart, folder, plain, "3-default", problems);

        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException e)
        {
            Logger.Info($"[SelfTest] could not remove the test chart {folder}: {e.Message}");
        }
    }

    /// <summary>Your look with one part (the gloves when they differ) swapped for the default character's.</summary>
    private static string EditOnePart(string yours, string plain, out string change)
    {
        var mine = yours.Split(':');
        var theirs = plain.Split(':');
        const int Outfit = 2;
        var outfit = mine.Length > Outfit ? mine[Outfit].Split(',') : [];
        var plainOutfit = theirs.Length > Outfit ? theirs[Outfit].Split(',') : [];
        var glove = Array.FindIndex(outfit, part => part.StartsWith('g'));
        if (glove >= 0 && glove < plainOutfit.Length && outfit[glove] != plainOutfit[glove])
        {
            change = $"the gloves, {outfit[glove]} -> {plainOutfit[glove]}";
            outfit[glove] = plainOutfit[glove];
            mine[Outfit] = string.Join(",", outfit);
            return string.Join(":", mine);
        }

        foreach (var g in Enumerable.Range(1, Math.Max(0, Math.Min(mine.Length, theirs.Length) - 1)))
        {
            var a = mine[g].Split(',');
            var b = theirs[g].Split(',');
            var i = Enumerable.Range(0, Math.Min(a.Length, b.Length)).FirstOrDefault(i => a[i] != b[i], -1);
            if (i < 0) continue;

            change = $"part {g}.{i}: {a[i]} -> {b[i]}";
            a[i] = b[i];
            mine[g] = string.Join(",", a);
            return string.Join(":", mine);
        }

        change = "nothing";
        return yours;
    }

    /// <summary>Plays a chart in the Ring until its actor is up, checks what it wears, and clips a close-up.</summary>
    private static IEnumerator PlayAndShoot(Chart chart, string folder, string expected, string shot, List<string> problems)
    {
        _keepYouAlive = true;
        var before = Mod.Actors.All.ToList();
        // No spot: the Ring lays the template's 12 m lane across its middle - from z = -4 it ran off the edge.
        Mod.Battle.Start(chart with { Arena = new ArenaSpec(ArenaKind.Ring) }, ArenaKind.Ring, folder);

        var deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        while (Mod.Battle.State != BattleState.Countdown && Time.realtimeSinceStartup < deadline) yield return null;
        // This battle's actor - the last one, despawned, can still be listed.
        Actor? actor = Mod.Actors.All.FirstOrDefault(a => a.Ready && a.Exists && !before.Contains(a));
        if (actor?.Player == null)
        {
            problems.Add($"{shot}: no actor came up");
            yield break;
        }

        yield return new WaitForSeconds(1f);
        var worn = actor.Player.Data.VisualData.ToPlayfabDataString();
        if (worn != expected) problems.Add($"{shot}: the boss wears \"{worn}\", not the chart's \"{expected}\"");
        else Logger.Info($"[SelfTest] look {shot}: the boss wears the chart's look");

        // Framed on the drawn body: an actor's head and controller transforms are not where it is drawn.
        SkinnedMeshRenderer? mesh = actor.Player.Controller.GetComponentInChildren<SkinnedMeshRenderer>();
        Vector3 chest = mesh != null ? mesh.bounds.center + (Vector3.up * 0.25f) : actor.Player.Controller.transform.position + (Vector3.up * 1.1f);
        Logger.Info($"[SelfTest] look {shot}: framing the body at {StringHelpers.Vector(chest)}{(mesh == null ? " (no mesh found)" : "")}");
        Vector3 towardYou = LaneOrigin is { } origin ? VectorHelpers.Flat(origin - chest) : Vector3.forward;
        _showCamera?.Invoke(chest + (towardYou * 1.8f) + (Vector3.up * 0.3f), chest);
        yield return new WaitForSeconds(0.3f);

        var shots = Path.Combine(MelonEnvironment.UserDataDirectory, "BossBattles", "SelfTestShots", "Look");
        Directory.CreateDirectory(shots);
        Shoot(shots, shot);

        Mod.Battle.Stop("look shot taken");
        _keepYouAlive = false;
        yield return new WaitForSeconds(1f);
    }
}
