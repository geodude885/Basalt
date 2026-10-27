using Basalt.Battle;
using Basalt.Services.Charts;
using Il2CppRUMBLE.Players.Subsystems;
using UnityEngine;

namespace Basalt.Presentation.Shaders;

internal class BattleVisualEvents(IShaderProvider? shaders)
{
    private long _lastBeat = -1;

    public float ShaderIntensity
    {
        get => shaders?.ShaderIntensity ?? 0f;
        set => shaders?.ShaderIntensity = Math.Clamp(value, 0f, 1f);
    }

    public void PreloadChartPacks(Chart chart) => shaders?.PreloadPacks(chart.Visuals.Select(v => v.Pack));

    public void Reset() => _lastBeat = -1;

    public void Update(Chart chart, double chartTime)
    {
        if (shaders == null) return;

        shaders.ApplyLooksAt(chart.Visuals, chartTime);

        shaders.SetGlobal("running", 1f);
        shaders.SetGlobal("time", (float)chartTime);

        if (BattlePlayer.Player?.Data is { } playerData) shaders.SetGlobal("health", playerData.HealthPoints / (float)PlayerHealth.MAX_PLAYER_HEALTH);

        if (chart.Tempo is not { } tempo) return;

        var beat = tempo.Beat(Math.Max(0, chartTime));
        shaders.SetGlobal("beat", (float)(beat - Math.Floor(beat)));

        var wholeBeat = (long)Math.Floor(beat);
        if (wholeBeat == _lastBeat) return;

        _lastBeat = wholeBeat;
        shaders.FireEvent("beat", BattlePlayer.Head, wholeBeat % 4 == 0 ? 1f : 0.6f, (float)(60.0 / tempo.BpmAt(chartTime)));
    }

    public void FireEvent(string name, Vector3 at, float strength, float seconds) => shaders?.FireEvent(name, at, strength, seconds);

    public void End()
    {
        if (shaders == null) return;

        shaders.ReleaseAllPacks();
        shaders.SetGlobal("running", 0f);
        shaders.SetGlobal("time", 0f);
        shaders.SetGlobal("beat", 0f);
        shaders.SetGlobal("health", 1f);
    }
}
