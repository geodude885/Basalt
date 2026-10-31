using System.Collections;
using Basalt.Actors;
using Basalt.Battle;
using Basalt.Services.Charts;
using Basalt.Services.Charts.Model;
using Il2CppRUMBLE.Players;
using MelonLoader.Utils;
using UnityEngine;

namespace Basalt.DevTools.Testing;

/// <summary>
/// <c>--basalt-selftest=look</c>: a new chart's boss stores and wears your look and stones, and
/// wears a changed look after editing one part. Close-ups go to <c>SelfTestShots/Look</c>.
/// </summary>
internal static partial class SelfTest
{
    private static IEnumerator RunLookTest(List<string> problems)
    {
        var localPlayer = BattlePlayer.Player!;
        var playerLook = localPlayer.Data.VisualData.ToPlayfabDataString();
        var plain = PlayerVisualData.Default.ToPlayfabDataString();
        var stones = ShiftstoneHelpers.EquippedBy(localPlayer.Controller);
        Logger.Info($"[SelfTest] look: yours \"{playerLook}\"{Environment.NewLine}default \"{plain}\"{Environment.NewLine}your stones: {string.Join(", ", stones)}");

        // 1. A chart made in game.
        var folder = Mod.Charts.NewChart();
        if (folder == null)
        {
            problems.Add("NEW CHART made no chart");
            yield break;
        }

        var file = Path.Combine(folder, ChartLibrary.FolderChartFile);
        var made = ChartLoader.Load(File.ReadAllText(file)).Chart;
        var boss = made?.Actors.Count > 0 ? made.Actors[0] : null;
        if (boss == null)
        {
            problems.Add("the new chart does not load");
        }
        else
        {
            if (boss.Look != playerLook) problems.Add($"the new chart's boss look is \"{boss.Look}\", not yours");
            if (!boss.Shiftstones.SequenceEqual(stones)) problems.Add($"the new chart's boss stones are [{string.Join(", ", boss.Shiftstones)}], not yours [{string.Join(", ", stones)}]");
        }

        if (made != null) yield return PlayAndShoot(made, folder, playerLook, "1-made", problems);

        // 2. One part of the look changed, as someone editing the chart would.
        var edited = EditOnePart(playerLook, plain, out var change);
        Logger.Info($"[SelfTest] look: changed {change}");
        if (edited == playerLook) problems.Add("could not find a part of your look to change");

        var text = File.ReadAllText(file);
        File.WriteAllText(file, text.Replace(playerLook, edited, StringComparison.Ordinal));
        var changed = ChartLoader.Load(File.ReadAllText(file)).Chart;
        if (changed?.Actors[0].Look != edited) problems.Add("the edited look was not read back from the chart");
        else yield return PlayAndShoot(changed, folder, edited, "2-edited", problems);

        // 3. The default character entirely - a look nothing like yours, to see one can change at all.
        File.WriteAllText(file, File.ReadAllText(file).Replace(edited, plain, StringComparison.Ordinal));
        var plainChart = ChartLoader.Load(File.ReadAllText(file)).Chart;
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
    private static string EditOnePart(string playerLook, string plain, out string change)
    {
        var playerLookParts = playerLook.Split(':');
        var defaultLookParts = plain.Split(':');
        const int Outfit = 2;
        var outfit = playerLookParts.Length > Outfit ? playerLookParts[Outfit].Split(',') : [];
        var plainOutfit = defaultLookParts.Length > Outfit ? defaultLookParts[Outfit].Split(',') : [];
        var glove = Array.FindIndex(outfit, part => part.StartsWith('g'));
        if (glove >= 0 && glove < plainOutfit.Length && outfit[glove] != plainOutfit[glove])
        {
            change = $"the gloves, {outfit[glove]} -> {plainOutfit[glove]}";
            outfit[glove] = plainOutfit[glove];
            playerLookParts[Outfit] = string.Join(",", outfit);
            return string.Join(":", playerLookParts);
        }

        foreach (var g in Enumerable.Range(1, Math.Max(0, Math.Min(playerLookParts.Length, defaultLookParts.Length) - 1)))
        {
            var a = playerLookParts[g].Split(',');
            var b = defaultLookParts[g].Split(',');
            var i = Enumerable.Range(0, Math.Min(a.Length, b.Length)).FirstOrDefault(i => a[i] != b[i], -1);
            if (i < 0) continue;

            change = $"part {g}.{i}: {a[i]} -> {b[i]}";
            a[i] = b[i];
            playerLookParts[g] = string.Join(",", a);
            return string.Join(":", playerLookParts);
        }

        change = "nothing";
        return playerLook;
    }

    /// <summary>Plays a chart in the Ring until its actor is up, checks what it wears, and clips a close-up.</summary>
    private static IEnumerator PlayAndShoot(Chart chart, string folder, string expected, string shot, List<string> problems)
    {
        _keepPlayerAlive = true;
        var before = Mod.Actors.Actors.ToList();
        // The template's 12 m lane fits across the Ring from this spot.
        Mod.Battle.Start(new BattleRequest(chart with { Arena = new ArenaSpec(ArenaKind.Ring, 0f, -6f, 0f) }) { AssetFolder = folder });

        var deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        while (Mod.Battle.Run?.State != BattleState.Countdown && Time.realtimeSinceStartup < deadline) yield return null;
        // This battle's actor - the last one, despawned, can still be listed.
        var actor = Mod.Actors.Actors.FirstOrDefault(a => a.Ready && !before.Contains(a));
        if (actor is not { Ready: true })
        {
            problems.Add($"{shot}: no actor came up");
            yield break;
        }

        yield return new WaitForSeconds(1f);
        var worn = actor.Body.Player.Data.VisualData.ToPlayfabDataString();
        if (worn != expected) problems.Add($"{shot}: the boss wears \"{worn}\", not the chart's \"{expected}\"");
        else Logger.Info($"[SelfTest] look {shot}: the boss wears the chart's look");

        // Framed on the drawn body: an actor's head and controller transforms are not where it is drawn.
        var mesh = actor.Body.Controller.GetComponentInChildren<SkinnedMeshRenderer>();
        var chest = mesh != null ? mesh.bounds.center + (Vector3.up * 0.25f) : actor.Body.Controller.transform.position + (Vector3.up * 1.1f);
        Logger.Info($"[SelfTest] look {shot}: framing the body at {chest.Describe()}{(mesh == null ? " (no mesh found)" : "")}");
        var towardPlayer = LaneOrigin is { } origin ? (origin - chest).Flat() : Vector3.forward;
        _showCamera?.Invoke(chest + (towardPlayer * 1.8f) + (Vector3.up * 0.3f), chest);
        yield return new WaitForSeconds(0.3f);

        var shots = Path.Combine(MelonEnvironment.UserDataDirectory, "Basalt", "SelfTestShots", "Look");
        Directory.CreateDirectory(shots);
        Shoot(shots, shot);

        Mod.Battle.Stop("look shot taken");
        _keepPlayerAlive = false;
        yield return new WaitForSeconds(1f);
    }
}
