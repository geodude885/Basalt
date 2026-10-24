using Basalt.Battle;
using Basalt.Services.Charts;
using Il2CppRUMBLE.Players.Subsystems;
using UnityEngine;

namespace Basalt.Presentation;

// A chart's RumbleReShaded looks, and the battle's state and events for packs to read. Does nothing without RumbleReShaded.
internal class BattleVisualEvents
{
    private readonly ReShade? _reshade = ReShade.IsLoaded ? new ReShade() : null;
    private long _lastBeat = -1;

    public float Strength
    {
        get => _reshade?.Strength ?? 0f;
        set => _reshade?.Strength = Math.Clamp(value, 0f, 1f);
    }

    public void PreloadChartPacks(Chart chart) => _reshade?.PreloadPacks(chart.Visuals.Select(v => v.Pack));

    public void Begin(Chart chart)
    {
        _lastBeat = -1;
        if (_reshade != null && chart.Visuals.Count > 0)
            Logger.Debug($"[Visuals.Chart] {chart.Visuals.Count} look(s); installed: {ReShade.DescribeInstalledPacks()}");
    }

    public void Update(Chart chart, double chartTime)
    {
        if (_reshade == null) return;

        _reshade.ApplyLooksAt(chart.Visuals, chartTime);

        ReShade.SetShaderGlobal("running", 1f);
        ReShade.SetShaderGlobal("time", (float)chartTime);

        if (BattlePlayer.Player?.Data is { } playerData) ReShade.SetShaderGlobal("health", playerData.HealthPoints / (float)PlayerHealth.MAX_PLAYER_HEALTH);

        if (chart.Tempo is not { } tempo) return;

        var beat = tempo.Beat(Math.Max(0, chartTime));
        ReShade.SetShaderGlobal("beat", (float)(beat - Math.Floor(beat)));

        var wholeBeat = (long)Math.Floor(beat);
        if (wholeBeat == _lastBeat) return;

        _lastBeat = wholeBeat;
        _reshade.FireEvent("beat", BattlePlayer.Head, wholeBeat % 4 == 0 ? 1f : 0.6f, (float)(60.0 / tempo.BpmAt(chartTime)));
    }

    public void Fire(string name, Vector3 at, float strength, float seconds) => _reshade?.FireEvent(name, at, strength, seconds);

    public void End()
    {
        if (_reshade == null) return;

        _reshade.ReleaseAllPacks();
        ReShade.SetShaderGlobal("running", 0f);
        ReShade.SetShaderGlobal("time", 0f);
        ReShade.SetShaderGlobal("beat", 0f);
        ReShade.SetShaderGlobal("health", 1f);
    }
}
