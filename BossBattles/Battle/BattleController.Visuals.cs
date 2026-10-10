using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using UnityEngine;

namespace BossBattles.Battle;

/// <summary>A chart's RumbleReShaded looks, and the battle state and events packs can read.</summary>
internal sealed partial class BattleController
{
    private readonly ReShade _reshade = new();
    private long _lastBeat = -1;

    public Color? LaneEdgeColour => _lane.EdgeColourNow;

    /// <summary>How strongly a chart's looks and signals apply: 0 off, 1 full.</summary>
    public float VisualsStrength
    {
        get => _reshade.Strength;
        set => _reshade.Strength = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>For the game quitting mid-run: your pack settings back before MelonLoader saves them.</summary>
    public void RestoreVisuals() => EndVisuals();

    /// <summary>The chart's looks now, the state globals, and a <c>beat</c> event on each beat (stronger on a bar's first).</summary>
    private void UpdateVisuals()
    {
        var t = ChartTime;
        _reshade.Apply(Chart!.Visuals, t);

        ReShade.SetGlobal("running", 1f);
        ReShade.SetGlobal("time", (float)t);
        Player? you = PlayerManager.instance?.localPlayer;
        if (you?.Data != null) ReShade.SetGlobal("health", you.Data.HealthPoints / (float)PlayerHealth.MAX_PLAYER_HEALTH);

        if (Chart.Tempo is not { } tempo) return;

        var beat = tempo.Beat(Math.Max(0, t));
        ReShade.SetGlobal("beat", (float)(beat - Math.Floor(beat)));
        var whole = (long)Math.Floor(beat);
        if (whole == _lastBeat) return;

        _lastBeat = whole;
        _reshade.Fire("beat", YourHead(), whole % 4 == 0 ? 1f : 0.6f, (float)(60.0 / tempo.BpmAt(t)));
    }

    private void EndVisuals()
    {
        _reshade.RestoreAll();
        ReShade.SetGlobal("running", 0f);
        ReShade.SetGlobal("beat", 0f);
    }
}
