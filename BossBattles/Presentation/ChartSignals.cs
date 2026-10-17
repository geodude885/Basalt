using BossBattles.Battle;
using BossBattles.Services.Charts;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using UnityEngine;

namespace BossBattles.Presentation;

/// <summary>A chart's RumbleReShaded looks, and the battle's state and events for packs to read.</summary>
internal class ChartSignals
{
    private readonly ReShade _reshade = new();
    private long _lastBeat = -1;

    /// <summary>How strongly a chart's looks and signals apply: 0 off, 1 full.</summary>
    public float Strength
    {
        get => _reshade.Strength;
        set => _reshade.Strength = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>The chart's packs loaded during the countdown, so the first look does not hitch.</summary>
    public void Prepare(Chart chart)
    {
        ReShade.Refresh();
        _reshade.Preload(chart.Visuals.Select(v => v.Pack));
    }

    public void Begin(Chart chart)
    {
        ReShade.Refresh();
        _lastBeat = -1;
        if (chart.Visuals.Count > 0) Logger.Debug($"[Visuals.Chart] {chart.Visuals.Count} look(s); installed: {ReShade.Describe()}");
    }

    /// <summary>The chart's looks now, the state globals, and a <c>beat</c> event on each beat (stronger on a bar's first).</summary>
    public void Update(Chart chart, double time)
    {
        _reshade.Apply(chart.Visuals, time);

        ReShade.SetGlobal("running", 1f);
        ReShade.SetGlobal("time", (float)time);
        Player? you = BattlePlayer.Player;
        if (you?.Data != null) ReShade.SetGlobal("health", you.Data.HealthPoints / (float)PlayerHealth.MAX_PLAYER_HEALTH);

        if (chart.Tempo is not { } tempo) return;

        var beat = tempo.Beat(Math.Max(0, time));
        ReShade.SetGlobal("beat", (float)(beat - Math.Floor(beat)));
        var whole = (long)Math.Floor(beat);
        if (whole == _lastBeat) return;

        _lastBeat = whole;
        Fire("beat", BattlePlayer.Head, whole % 4 == 0 ? 1f : 0.6f, (float)(60.0 / tempo.BpmAt(time)));
    }

    /// <summary>An event at a world point, as <c>bossbattles.&lt;name&gt;</c>.</summary>
    public void Fire(string name, Vector3 at, float strength, float seconds) => _reshade.Fire(name, at, strength, seconds);

    /// <summary>Your pack settings back, and the globals off; also for the game quitting mid-run, before MelonLoader saves.</summary>
    public void End()
    {
        _reshade.RestoreAll();
        ReShade.SetGlobal("running", 0f);
        ReShade.SetGlobal("beat", 0f);
    }
}
