using BossBattles.Services.Charts;
using RumbleReShaded.Api;
using UnityEngine;

namespace BossBattles.Presentation;

// Everything that touches RumbleReShaded: only ever created once it is loaded, so nothing here runs without it.
internal class ReShade
{
    private const string ModId = "bossbattles";

    private readonly Dictionary<string, PackOverride?> _overridesByPack = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<PackInfo> _installedPacks = [];

    // An older RumbleReShaded without pack overrides counts as absent: calling into it would throw every frame.
    public static bool IsLoaded =>
        AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RumbleReShaded")?.GetType("RumbleReShaded.Api.PackOverride") != null;

    // How strongly looks and events apply: 0 off, 1 full.
    public float Strength { get; set; } = 1f;

    public void FireEvent(string name, Vector3 at, float strength, float seconds)
    {
        if (Strength > 0f) RRS.Fire($"{ModId}.{name}", at, strength * Strength, seconds);
    }

    // Read in shaders as _RRS_bossbattles_<name>.
    public static void SetShaderGlobal(string name, float value) => RRS.SetGlobal($"{ModId}.{name}", value);

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
            packOverride.Weight = look.On ? Strength : 0f;
            packOverride.Enabled = true;
            foreach ((var parameter, var value) in look.Values) packOverride.Set(parameter, value);
        }
    }

    public void ReleaseAllPacks()
    {
        foreach (var packOverride in _overridesByPack.Values) packOverride?.Dispose();
        _overridesByPack.Clear();
    }

    public static string DescribeInstalledPacks() =>
        RRS.Packs.Count == 0 ? "RumbleReShaded has no packs" : string.Join("; ", RRS.Packs.Select(p => $"{p.Name} ({string.Join(", ", p.Parameters.Select(q => q.Label))})"));

    private float PlayerValue(string packName, string parameter)
    {
        var pack = _installedPacks.FirstOrDefault(p => string.Equals(p.Name, packName, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Id, packName, StringComparison.OrdinalIgnoreCase));
        var wanted = parameter.Squash();
        return pack?.Parameters.FirstOrDefault(p => p.Label.Squash() == wanted || p.Property.Squash() == wanted)?.PlayerValue ?? 0f;
    }
}
