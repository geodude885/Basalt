using System.Collections;
using System.Runtime.CompilerServices;
using Basalt.Battle;
using Basalt.Presentation.Shaders;
using Basalt.Services.Charts;
using MelonLoader;
using RumbleReShaded.Api;
using UnityEngine;

namespace Basalt.DevTools.Testing;

// With RumbleReShaded: a chart look must be the pack's value in use mid-run, the player's own settings untouched,
// and the pack handed back after. RRS draws only on a VR camera, so the values are checked, not the picture.
internal static partial class SelfTest
{
    private const string LookPack = "Grayscale";
    private const string LookParameter = "Strength";
    private const float LookStrength = 0.25f;

    private static string? _visualsProblem;

    private static Chart WithLook(Chart chart)
    {
        if (!ReShade.IsLoaded || !HasLookPack())
        {
            Logger.Info($"[SelfTest] no RumbleReShaded {LookPack} pack - chart looks not checked");
            return chart;
        }

        var cue = new VisualCue(0.5, LookPack, new Dictionary<string, float> { [LookParameter] = LookStrength }, Off: false, Fade: 0.5);
        MelonCoroutines.Start(CheckLook());
        return chart with { Visuals = new[] { cue } };
    }

    // Kept apart from WithLook so RumbleReShaded's types are only touched once it is known to be loaded.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool HasLookPack() => RRS.Packs.Any(p => p.Name == LookPack);

    private static IEnumerator CheckLook()
    {
        (bool Enabled, float Value) PlayerSetting()
        {
            var pack = RRS.Packs.First(p => p.Name == LookPack);
            return (pack.PlayerEnabled, pack.Parameters.First(p => p.Label == LookParameter).PlayerValue);
        }

        (bool Enabled, float Value) InUse()
        {
            var pack = RRS.Packs.First(p => p.Name == LookPack);
            return (pack.Enabled, pack.Parameters.First(p => p.Label == LookParameter).Value);
        }

        var playerBefore = PlayerSetting();

        var deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        while ((Mod.Battle.State != BattleState.Running || Mod.Battle.ChartTime < 1.5) && Time.realtimeSinceStartup < deadline) yield return null;

        var midRun = InUse();
        var expected = playerBefore.Value + ((LookStrength - playerBefore.Value) * Mod.Battle.Visuals.ShaderIntensity);
        if (!midRun.Enabled || Math.Abs(midRun.Value - expected) > 0.01f)
            _visualsProblem = $"chart look: {LookPack} was {(midRun.Enabled ? "on" : "off")} at {LookParameter} {midRun.Value:0.00} mid-run, not on at {expected:0.00}";
        else if (PlayerSetting() != playerBefore)
            _visualsProblem = $"chart look wrote the player's own {LookPack} settings";
        else
            Logger.Info($"[SelfTest] chart look on: {LookPack} at {midRun.Value:0.00} (yours {playerBefore.Value:0.00}, {(playerBefore.Enabled ? "on" : "off")}, untouched)");

        while (Mod.Battle.State == BattleState.Running && Time.realtimeSinceStartup < deadline) yield return null;
        yield return null;

        var afterRun = InUse();
        if (afterRun.Enabled != playerBefore.Enabled || Math.Abs(afterRun.Value - playerBefore.Value) > 1e-4f)
            _visualsProblem ??= $"chart look not handed back: {LookPack} {(afterRun.Enabled ? "on" : "off")} at {afterRun.Value:0.00}, yours is {(playerBefore.Enabled ? "on" : "off")} at {playerBefore.Value:0.00}";
    }
}
