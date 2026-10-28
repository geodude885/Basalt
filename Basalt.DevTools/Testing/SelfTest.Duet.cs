using System.Collections;
using Basalt.Battle;
using Basalt.Editing;
using Basalt.Services.Charts;
using MelonLoader.Utils;
using UnityEngine;

namespace Basalt.DevTools.Testing;

// duet: one actor recorded, then the other recorded with the first there as a guide, then both played together.
internal static partial class SelfTest
{
    private const string DuetChart = /*lang=json*/ """
        {
          "format": 1,
          "name": "Duet Test",
          "arena": { "map": "ring", "at": { "x": 0, "z": -4 }, "facing": 0 },
          "lane": { "length": 8, "width": 4 },
          "actors": [
            { "id": "lead", "name": "Lead", "at": { "x": -1, "z": 8 } },
            { "id": "partner", "name": "Partner", "at": { "x": 1, "z": 8 } }
          ],
          "end": 3
        }
        """;

    private static IEnumerator RunDuetTest(List<string> problems)
    {
        var folder = Path.Combine(MelonEnvironment.UserDataDirectory, "Basalt", "SelfTest", "duet-test");
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, ChartLibrary.FolderChartFile);
        File.WriteAllText(file, DuetChart);

        yield return RecordAndKeep(file, "lead", null, problems);
        yield return RecordAndKeep(file, "partner", "lead", problems);

        var duet = Reloaded(file).Result.Chart;
        var tracks = duet?.Tracks.Select(t => $"{t.ActorId}: {string.Join(", ", t.Clips.Select(c => c.Clip))}").ToList() ?? [];
        Logger.Info($"[SelfTest] duet tracks: {string.Join(" | ", tracks)}");
        if (duet == null || duet.Tracks.Count != 2 || duet.Tracks.Any(t => t.Clips.Count != 1))
        {
            problems.Add($"keeping both takes left {tracks.Count} track(s): {string.Join(" | ", tracks)}");
            yield break;
        }

        // Played back, both perform at once.
        Mod.Battle.Start(new BattleRequest(duet) { AssetFolder = folder });
        var deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        while (Mod.Battle.Run?.State != BattleState.Running && Time.realtimeSinceStartup < deadline) yield return null;
        yield return new WaitForSeconds(0.8f);

        var performing = Mod.Actors.Actors.Where(a => a.Ready && a.Body.Clips.Playing).Select(a => a.Id).ToList();
        Logger.Info($"[SelfTest] duet playback: {string.Join(", ", performing)} performing");
        if (performing.Count != 2) problems.Add($"played back, {performing.Count} of 2 actors performed ({string.Join(", ", performing)})");
        Mod.Battle.Stop("duet checked");
    }

    // Records the actor `actorId` walking, and keeps it; with `guideId`, that actor must be there performing, mimed.
    private static IEnumerator RecordAndKeep(string file, string actorId, string? guideId, List<string> problems)
    {
        Mod.Studio.StartRecording(Reloaded(file), actorId);

        var deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        while (Mod.Battle.Run?.State != BattleState.Running && Time.realtimeSinceStartup < deadline) yield return null;
        yield return new WaitForSeconds(0.8f);

        if (Mod.Battle.Run?.Request.Performer.Id != actorId) problems.Add($"recording {actorId} put you in {Mod.Battle.Run?.Request.Performer.Id}'s place");

        var present = Mod.Actors.Actors.Where(a => a.Ready).ToList();
        var guide = present.FirstOrDefault(a => a.Id == guideId);
        Logger.Info($"[SelfTest] recording {actorId}: actors there {string.Join(", ", present.Select(a => $"{a.Id}{(a.Body?.Clips.Playing == true ? " (performing)" : "")}"))}");
        if (present.Any(a => a.Id == actorId)) problems.Add($"recording {actorId}, the actor you replace was spawned too");
        if (guideId != null && guide?.Body?.Clips.Playing != true) problems.Add($"recording {actorId}, {guideId} was not there performing its take");
        if (guideId == null && present.Count > 1) problems.Add($"recording the first take, {present.Count - 1} other actor(s) had nothing to perform but were there");

        var movesBefore = Logged("Actor.Move");
        yield return Walk(new Vector2(1f, 0f), 1f);
        if (guideId != null && Logged("Actor.Move") != movesBefore) problems.Add($"the guide {guideId} ran moves while you recorded - it should be mimed");

        if (!PushGameButton("stop")) problems.Add($"no STOP button while recording {actorId}");
        deadline = Time.realtimeSinceStartup + 60f;
        while (Mod.Studio.State == StudioState.Reviewing && Time.realtimeSinceStartup < deadline) yield return null;

        yield return new WaitForSeconds(1.2f);
        if (!PushGameButton("keep")) problems.Add($"no KEEP button after recording {actorId}");
        yield return new WaitForSeconds(0.5f);
    }
}
