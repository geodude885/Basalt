using System.Collections;
using Basalt.Battle;
using Basalt.DevTools.Flatscreen;
using Il2CppRUMBLE.Players.Subsystems;
using UnityEngine;

namespace Basalt.DevTools.Testing;

internal static partial class SelfTest
{
    // Turned on, watch mode plays the chosen chart from the overview with you on full health; turned off, the view goes.
    private static IEnumerator RunWatchTest(List<string> problems)
    {
        _watcher!.Toggle();

        var deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        while (Mod.Battle.Run?.State != BattleState.Running && Time.realtimeSinceStartup < deadline) yield return null;
        if (!_watcher.Watching)
        {
            problems.Add("turning watch mode on did not start the chosen chart");
            if (_watcher.On) _watcher.Toggle();
            yield break;
        }

        yield return new WaitForSeconds(6f);

        var camera = _desktopCamera!.Camera;
        var view = ChartWatcher.Overview();
        if (camera == null || view == null || Vector3.Distance(camera.transform.position, view.Value.Position) > 0.05f)
            problems.Add("watching, the desktop camera was not at the overview");

        var health = BattlePlayer.Player?.Data.HealthPoints ?? 0;
        Logger.Info($"[SelfTest] watching {Mod.Battle.Run?.Request.Chart.Name}: {health} HP, {Mod.Battle.Run?.Score.DamageTaken} damage taken");
        if (health < PlayerHealth.MAX_PLAYER_HEALTH) problems.Add($"watching, you were on {health} HP, not kept full");
        Shoot(ShotFolder(), "0-watch");

        _watcher.Toggle();
        yield return null;
        if (_desktopCamera.Active) problems.Add("turning watch mode off left the desktop camera on");
        Mod.Battle.Stop("watch checked");
    }
}
