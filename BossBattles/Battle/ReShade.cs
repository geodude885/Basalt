using System.Collections;
using System.Reflection;
using BossBattles.Core;
using BossBattles.Services.Charts;
using MelonLoader;
using UnityEngine;

namespace BossBattles.Battle;

/// <summary>
/// RumbleReShaded (RRS), when it is installed: a chart's shader looks and the battle's signals.
/// Found by reflection, so BossBattles neither needs it nor references it, and nothing here runs
/// without it.
/// <para><b>Looks</b>: RRS has no call for another mod to switch a pack or set its sliders, but it
/// reads both from its own MelonPreferences entries every frame - so a chart's look is written
/// there (found through <c>RumbleReShadedMod.Packs</c>, never by guessing names), and the player's
/// own values are put back when the run ends, stops, changes scene or the game quits.</para>
/// <para><b>Signals</b>, for packs to react to: events through <c>RRS.Fire</c> on
/// <c>bossbattles.*</c> channels, and floats as <c>_RRS_bossbattles_*</c> shader globals - RRS's
/// own <c>SetGlobal</c> requires a dotted key and publishes <c>_RRS_mod.key</c>, which no shader
/// can declare. See docs/chart-format.md.</para>
/// <para><see cref="Strength"/> (the Chart visuals setting) scales all of it; 0 is off.</para>
/// </summary>
internal sealed class ReShade
{
    private static bool _searched;
    private static MethodInfo? _fire;
    private static PropertyInfo? _ready;
    private static PropertyInfo? _packs;

    private readonly Dictionary<object, Original> _originals = [];
    private readonly HashSet<string> _reported = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>0 (off) to 1: how strongly looks and signals apply.</summary>
    public float Strength { get; set; } = 1f;

    public static bool Installed
    {
        get
        {
            Find();
            return _fire != null;
        }
    }

    private static bool Ready => _ready?.GetValue(null) is true;

    /// <summary>A one-shot event for packs listening on <paramref name="name"/> (sent as "bossbattles.name").</summary>
    public void Fire(string name, Vector3 at, float strength = 1f, float duration = 1f, float radius = 0f)
    {
        if (Strength <= 0f || !Installed || !Ready) return;

        try
        {
            _fire!.Invoke(null, new object[] { $"bossbattles.{name}", at, radius, strength * Strength, duration });
        }
        catch (Exception e)
        {
            Once("Visuals.FireFailed", e.InnerException?.Message ?? e.Message);
        }
    }

    /// <summary>A float any shader can read as <c>_RRS_bossbattles_name</c>; scaled by nothing - it is state, not a look.</summary>
    public static void SetGlobal(string name, float value) => Shader.SetGlobalFloat($"_RRS_bossbattles_{name}", value);

    // ---- Looks -----------------------------------------------------------------------------------

    /// <summary>Applies the chart's looks at chart time <paramref name="time"/>.</summary>
    public void Apply(IReadOnlyList<VisualCue> cues, double time)
    {
        if (cues.Count == 0 || Strength <= 0f || !Installed || !Ready || !MasterEnabled()) return;

        try
        {
            Dictionary<string, PackLook> looks = LookTimeline.Evaluate(cues, time, Baseline);
            foreach ((var name, PackLook look) in looks)
            {
                Pack? pack = FindPack(name);
                if (pack == null)
                {
                    Once($"Visuals.NoPack:{name}", $"no RumbleReShaded pack called \"{name}\" is installed - its looks are skipped");
                    continue;
                }

                Original original = Remember(pack);
                if (!look.On)
                {
                    Restore(pack, original);
                    continue;
                }

                Set(pack.Enabled, true);
                foreach ((var key, var value) in look.Values)
                {
                    Param? param = pack.Find(key);
                    if (param == null)
                    {
                        Once($"Visuals.NoParameter:{name}.{key}", $"pack \"{name}\" has no parameter \"{key}\" (it has {string.Join(", ", pack.Params.Select(p => p.Label))})");
                        continue;
                    }

                    // The setting scales the chart's change, not the value: at 0.5 a look goes halfway from yours.
                    var yours = original.Values.TryGetValue(param.Entry, out var v) ? v : param.Entry.Value;
                    Set(param.Entry, Mathf.Clamp(yours + ((value - yours) * Strength), param.Min, param.Max));
                }
            }
        }
        catch (Exception e)
        {
            Once("Visuals.Failed", e.ToString());
        }
    }

    /// <summary>
    /// Loads the chart's packs before they are needed. RRS loads a pack's AssetBundle and material
    /// synchronously, in its render pass, on the first frame the pack is on - a hitch mid-battle if a
    /// look first switches a pack on there. Done in the countdown instead; once loaded a pack stays
    /// loaded (switched off, it is only skipped), so later switches cost nothing.
    /// </summary>
    public void Preload(IEnumerable<string> packNames)
    {
        if (Strength <= 0f || !Installed || !Ready || !MasterEnabled()) return;

        foreach (var name in packNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Pack? pack = FindPack(name);
            MethodInfo? load = pack?.Handle.GetType().GetMethod("EnsureLoaded", Type.EmptyTypes);
            if (pack == null || load == null) continue;

            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var loaded = load.Invoke(pack.Handle, null) is true;
                Mod.Note(loaded ? "Visuals.Preloaded" : "Visuals.PreloadFailed", $"{pack.Name}: {(loaded ? "loaded" : "did not load")} in {clock.Elapsed.TotalMilliseconds:0.0} ms", echo: !loaded);
            }
            catch (Exception e)
            {
                Once("Visuals.PreloadFailed", e.InnerException?.Message ?? e.Message);
            }
        }
    }

    /// <summary>Puts back every pack setting a chart changed - its on/off and every slider.</summary>
    public void RestoreAll()
    {
        if (_originals.Count == 0) return;

        foreach (Original original in _originals.Values)
        {
            try
            {
                Set(original.Enabled.Entry, original.Enabled.Value);
                foreach ((MelonPreferences_Entry<float> entry, var value) in original.Values) Set(entry, value);
            }
            catch (Exception e)
            {
                Once("Visuals.RestoreFailed", e.Message);
            }
        }

        Mod.Note("Visuals.Restored", $"{_originals.Count} pack(s) put back to your settings", echo: false);
        _originals.Clear();
    }

    /// <summary>The installed packs and their parameters, for the log - what a chart can name.</summary>
    public static string Describe()
    {
        if (!Installed) return "RumbleReShaded is not installed";
        List<Pack> packs = Packs();
        return packs.Count == 0 ? "RumbleReShaded has no packs" : string.Join("; ", packs.Select(p => $"{p.Name} ({string.Join(", ", p.Params.Select(q => q.Label))})"));
    }

    private float Baseline(string packName, string key)
    {
        Pack? pack = FindPack(packName);
        Param? param = pack?.Find(key);
        return pack == null || param == null ? 0f : Remember(pack).Values.TryGetValue(param.Entry, out var v) ? v : param.Entry.Value;
    }

    private Original Remember(Pack pack)
    {
        if (_originals.TryGetValue(pack.Handle, out Original? known)) return known;

        var original = new Original(
            (pack.Enabled, pack.Enabled.Value),
            pack.Params.ToDictionary(p => p.Entry, p => p.Entry.Value));
        _originals[pack.Handle] = original;
        Mod.Note("Visuals.Pack", $"{pack.Name}: taken over for the run (yours: {(original.Enabled.Value ? "on" : "off")})", echo: false);
        return original;
    }

    private static void Restore(Pack pack, Original original)
    {
        Set(pack.Enabled, original.Enabled.Value);
        foreach ((MelonPreferences_Entry<float> entry, var value) in original.Values) Set(entry, value);
    }

    /// <summary>Writes only a real change - each write raises the entry's changed event, which the menu redraws on.</summary>
    private static void Set(MelonPreferences_Entry<bool> entry, bool value)
    {
        if (entry.Value != value) entry.Value = value;
    }

    private static void Set(MelonPreferences_Entry<float> entry, float value)
    {
        if (Math.Abs(entry.Value - value) > 1e-4f) entry.Value = value;
    }

    private static bool MasterEnabled() => MelonPreferences.GetEntry<bool>("RumbleReShaded", "Enabled")?.Value ?? true;

    private void Once(string key, string text)
    {
        if (_reported.Add(key)) Mod.Note(key.Split(':')[0], text);
    }

    private static Pack? FindPack(string name) =>
        Packs().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? Packs().FirstOrDefault(p => string.Equals(p.Id, name, StringComparison.OrdinalIgnoreCase));

    private static List<Pack>? _packCache;

    /// <summary>Reads the pack list again - RRS's "Reload packs" can change it between runs.</summary>
    public static void Refresh() => _packCache = null;

    private static List<Pack> Packs() => _packCache ??= ReadPacks().ToList();

    private static IEnumerable<Pack> ReadPacks()
    {
        if (!Installed || _packs?.GetValue(null) is not IEnumerable list) yield break;

        foreach (var pack in list)
        {
            Type type = pack.GetType();
            if (type.GetField("EnabledSetting")?.GetValue(pack) is not MelonPreferences_Entry<bool> enabled) continue;

            var parameters = new List<Param>();
            if (type.GetField("Parameters")?.GetValue(pack) is IEnumerable items)
            {
                foreach (var item in items)
                {
                    Type t = item.GetType();
                    if (t.GetField("Setting")?.GetValue(item) is not MelonPreferences_Entry<float> entry) continue;
                    parameters.Add(new Param(
                        t.GetField("Property")?.GetValue(item) as string ?? "",
                        t.GetField("Label")?.GetValue(item) as string ?? "",
                        t.GetField("Min")?.GetValue(item) is float min ? min : float.MinValue,
                        t.GetField("Max")?.GetValue(item) is float max ? max : float.MaxValue,
                        entry));
                }
            }

            yield return new Pack(pack, type.GetField("Name")?.GetValue(pack) as string ?? "", type.GetField("Id")?.GetValue(pack) as string ?? "", enabled, parameters);
        }
    }

    private static void Find()
    {
        if (_searched) return;
        _searched = true;

        try
        {
            Assembly? rrs = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RumbleReShaded");
            Type? api = rrs?.GetType("RumbleReShaded.Api.RRS");
            Type? mod = rrs?.GetType("RumbleReShaded.RumbleReShadedMod");
            if (api == null || mod == null)
            {
                Mod.Note("Visuals.NoReShade", rrs == null ? "RumbleReShaded is not installed - chart looks and battle signals are off" : "this RumbleReShaded has no API (1.2.0+ needed)", echo: false);
                return;
            }

            _fire = api.GetMethod("Fire", new[] { typeof(string), typeof(Vector3), typeof(float), typeof(float), typeof(float) });
            _ready = api.GetProperty("Ready");
            _packs = mod.GetProperty("Packs");
            Mod.Note("Visuals.ReShade", $"RumbleReShaded {rrs!.GetName().Version} found (API {api.GetProperty("ApiVersion")?.GetValue(null)})", echo: false);
        }
        catch (Exception e)
        {
            _fire = null;
            Mod.Note("Visuals.NoReShade", $"RumbleReShaded could not be read: {e.Message}");
        }
    }

    private sealed record Pack(object Handle, string Name, string Id, MelonPreferences_Entry<bool> Enabled, List<Param> Params)
    {
        /// <summary>A parameter by label ("Chromatic Aberration"), property ("_ChromAb") or property without its underscore ("ChromAb").</summary>
        public Param? Find(string key)
        {
            var wanted = Squash(key);
            return Params.FirstOrDefault(p => Squash(p.Label) == wanted || Squash(p.Property) == wanted);
        }

        private static string Squash(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    private sealed record Param(string Property, string Label, float Min, float Max, MelonPreferences_Entry<float> Entry);

    private sealed record Original((MelonPreferences_Entry<bool> Entry, bool Value) Enabled, Dictionary<MelonPreferences_Entry<float>, float> Values);
}
