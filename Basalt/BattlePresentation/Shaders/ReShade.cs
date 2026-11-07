using Basalt.Services.Charts;
using Basalt.Services.Charts.Model;
using MelonLoader;
using RumbleReShaded.Api;
using Semver;
using UnityEngine;

namespace Basalt.BattlePresentation.Shaders;

internal class ReShade : IShaderProvider
{
    private const string ModId = "basalt";

    public float ShaderIntensity { get; set; } = 1f;
    private static readonly SemVersion MinVersion = SemVersion.Parse("1.2.0-dev.2");
    public static bool IsLoaded =>
        MelonBase.RegisteredMelons.FirstOrDefault(m => m.Info.Name == "RumbleReShaded")?.Info.SemanticVersion is { } version && version >= MinVersion;

    private const string WeightKey = "\0weight";

    private readonly Dictionary<string, PackOverride?> _overridesByPack = new(StringComparer.OrdinalIgnoreCase);
    private readonly VisualFlashingReducer _smoother = new();
    private LookTimeline? _timeline;
    private IReadOnlyList<ShaderCue>? _timelineCues;

    public float FlashingSmoothingDelaySeconds
    {
        get => _smoother.Delay;
        set => _smoother.Delay = value;
    }
    private IReadOnlyList<PackInfo> _installedPacks = [];

    public static IShaderProvider? CreateIfLoaded() => IsLoaded ? new ReShade() : null;

    public void FireEvent(string name, Vector3 at, float strength, float seconds)
    {
        if (ShaderIntensity > 0f) RRS.Fire($"{ModId}.{name}", at, strength * ShaderIntensity, seconds);
    }

    // Read in shaders as _RRS_basalt_<name>.
    public void SetGlobal(string name, float value) => RRS.SetGlobal($"{ModId}.{name}", value);

    public void PreloadPacks(IEnumerable<string> packNames)
    {
        _installedPacks = RRS.Packs;
        foreach (var name in packNames.Distinct(StringComparer.OrdinalIgnoreCase)) RRS.Preload(name);
    }

    public void ApplyLooksAt(IReadOnlyList<ShaderCue> cues, double chartTime)
    {
        if (cues.Count == 0) return;

        if (!ReferenceEquals(cues, _timelineCues))
        {
            _timeline = new LookTimeline(cues, PlayerValue);
            _timelineCues = cues;
        }

        var deltaTime = Time.deltaTime;
        foreach ((var packName, var look) in _timeline!.VisualsAt(chartTime))
        {
            if (!_overridesByPack.TryGetValue(packName, out var packOverride))
                _overridesByPack[packName] = packOverride = RRS.Override(ModId, packName);
            if (packOverride == null) continue;

            // An "off" cue hands the pack back to the player; Strength blends the chart's look over theirs.
            packOverride.Weight = _smoother.WithFlashingReduction(packName, WeightKey, look.On ? 1f : 0f, deltaTime, 0f) * ShaderIntensity;
            packOverride.Enabled = true;
            foreach ((var parameter, var value) in look.Values)
                packOverride.Set(parameter, _smoother.WithFlashingReduction(packName, parameter, value, deltaTime, _timeline.PlayerDefaultParameters(packName, parameter)));
        }
    }

    public void ReleaseAllPacks()
    {
        foreach (var packOverride in _overridesByPack.Values) packOverride?.Dispose();
        _overridesByPack.Clear();
        _smoother.Reset();
        _timeline = null;
        _timelineCues = null;
    }

    private float PlayerValue(string packName, string parameter)
    {
        var pack = _installedPacks.FirstOrDefault(p => string.Equals(p.Name, packName, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Id, packName, StringComparison.OrdinalIgnoreCase));
        var wanted = parameter.Squash();
        return pack?.Parameters.FirstOrDefault(p => p.Label.Squash() == wanted || p.Property.Squash() == wanted)?.PlayerValue ?? 0f;
    }
}
