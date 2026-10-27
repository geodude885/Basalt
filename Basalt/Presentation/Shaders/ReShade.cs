using Basalt.Services.Charts;
using MelonLoader;
using RumbleReShaded.Api;
using Semver;
using UnityEngine;

namespace Basalt.Presentation.Shaders;

internal class ReShade : IShaderProvider
{
    private const string ModId = "basalt";

    public float ShaderIntensity { get; set; } = 1f;
    private static readonly SemVersion MinVersion = SemVersion.Parse("1.2.0-dev.2");
    public static bool IsLoaded =>
        MelonBase.RegisteredMelons.FirstOrDefault(m => m.Info.Name == "RumbleReShaded")?.Info.SemanticVersion is { } version && version >= MinVersion;

    private readonly Dictionary<string, PackOverride?> _overridesByPack = new(StringComparer.OrdinalIgnoreCase);
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

    public void ApplyLooksAt(IReadOnlyList<VisualCue> cues, double chartTime)
    {
        if (cues.Count == 0) return;

        foreach ((var packName, var look) in LookTimeline.Evaluate(cues, chartTime, PlayerValue))
        {
            if (!_overridesByPack.TryGetValue(packName, out var packOverride))
                _overridesByPack[packName] = packOverride = RRS.Override(ModId, packName);
            if (packOverride == null) continue;

            // An "off" cue hands the pack back to the player; Strength blends the chart's look over theirs.
            packOverride.Weight = look.On ? ShaderIntensity : 0f;
            packOverride.Enabled = true;
            foreach ((var parameter, var value) in look.Values) packOverride.Set(parameter, value);
        }
    }

    public void ReleaseAllPacks()
    {
        foreach (var packOverride in _overridesByPack.Values) packOverride?.Dispose();
        _overridesByPack.Clear();
    }

    private float PlayerValue(string packName, string parameter)
    {
        var pack = _installedPacks.FirstOrDefault(p => string.Equals(p.Name, packName, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Id, packName, StringComparison.OrdinalIgnoreCase));
        var wanted = parameter.Squash();
        return pack?.Parameters.FirstOrDefault(p => p.Label.Squash() == wanted || p.Property.Squash() == wanted)?.PlayerValue ?? 0f;
    }
}
