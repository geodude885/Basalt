using System.Collections;
using Basalt.Services.Scoring;
using UnityEngine;

namespace Basalt.DevTools.Testing;

internal static partial class SelfTest
{
    /// <summary>The results board at each look of rank, as screenshots; run with <c>--basalt-shots</c> off or on.</summary>
    private static IEnumerator RunRanksTest(List<string> problems)
    {
        if (You is not { } controller)
        {
            problems.Add("no local player");
            yield break;
        }

        var rig = PlayerRig.From(controller);
        var board = BasaltMod.Battle.Board;
        board.SetPosition(rig.Head.position + (rig.Facing * 3f), rig.Head.position);

        foreach (var rank in new[] { "A+", "S", "SS", "SS+" })
        {
            board.ShowResult(new BattleResult("Rank test", true, 60, 0, 0, 12, 20, 1120, rank), "NEW BEST");
            yield return new WaitForSeconds(0.4f);
            ShootPanel(board.GameObject!.transform, $"0-rank-{rank.Replace("+", "plus", StringComparison.Ordinal)}");
        }

        board.Hide();
    }
}
