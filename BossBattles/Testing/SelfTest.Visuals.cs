using System.Collections;
using BossBattles.Battle;
using BossBattles.Core;
using BossBattles.Services.Charts;
using MelonLoader;
using UnityEngine;

namespace BossBattles.Testing;

/// <summary>
/// With RumbleReShaded installed: the self-test chart gets a look - its Grayscale pack faded to a
/// strength - which must be on the pack's own settings mid-run and gone from them after, every
/// value back as the player had it. RRS draws only on a VR camera, so this checks the settings a
/// look writes, not the picture. RRS's master switch is turned on for the run if it was off, and
/// put back.
/// </summary>
internal static partial class SelfTest
{
    private const string LookPack = "Grayscale";
    private const string LookCategory = "RRS_Grayscale";
    private const float LookStrength = 0.25f;

    private static string? _visualsProblem;

    /// <summary>The chart with a look added, when RRS and its Grayscale pack are there to show it.</summary>
    private static Chart WithLook(Chart chart)
    {
        if (!ReShade.Installed || MelonPreferences.GetEntry<float>(LookCategory, "_Strength") == null)
        {
            Mod.Say($"[SelfTest] no RumbleReShaded {LookPack} pack - chart looks not checked");
            return chart;
        }

        var cue = new VisualCue(0.5, LookPack, new Dictionary<string, float> { ["Strength"] = LookStrength }, Off: false, Fade: 0.5);
        MelonCoroutines.Start(CheckLook());
        return chart with { Visuals = new[] { cue } };
    }

    private static IEnumerator CheckLook()
    {
        MelonPreferences_Entry<bool>? master = MelonPreferences.GetEntry<bool>("RumbleReShaded", "Enabled");
        MelonPreferences_Entry<bool> enabled = MelonPreferences.GetEntry<bool>(LookCategory, "Enabled");
        MelonPreferences_Entry<float> strength = MelonPreferences.GetEntry<float>(LookCategory, "_Strength");
        var masterWas = master?.Value ?? true;
        var enabledWas = enabled.Value;
        var strengthWas = strength.Value;
        master?.Value = true;

        var deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        while ((Mod.Battle.State != BattleState.Running || Mod.Battle.ChartTime < 1.5) && Time.realtimeSinceStartup < deadline) yield return null;

        var expected = strengthWas + ((LookStrength - strengthWas) * Mod.Battle.VisualsStrength);
        if (!enabled.Value || Math.Abs(strength.Value - expected) > 0.01f)
            _visualsProblem = $"chart look: {LookPack} was {(enabled.Value ? "on" : "off")} at strength {strength.Value:0.00} mid-run, not on at {expected:0.00}";
        else
            Mod.Say($"[SelfTest] chart look on: {LookPack} at {strength.Value:0.00} (yours {strengthWas:0.00}, {(enabledWas ? "on" : "off")})");

        while (Mod.Battle.State == BattleState.Running && Time.realtimeSinceStartup < deadline) yield return null;
        yield return null;

        if (enabled.Value != enabledWas || Math.Abs(strength.Value - strengthWas) > 1e-4f)
            _visualsProblem ??= $"chart look not undone: {LookPack} {(enabled.Value ? "on" : "off")} at {strength.Value:0.00}, yours was {(enabledWas ? "on" : "off")} at {strengthWas:0.00}";

        master?.Value = masterWas;
    }
}
