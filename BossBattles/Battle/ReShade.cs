using System.Collections;
using System.Reflection;
using BossBattles.Services.Charts;
using MelonLoader;
using UnityEngine;

namespace BossBattles.Battle;

/// <summary>
/// RumbleReShaded, found by reflection when installed: a chart's shader looks and the battle's signals.
/// <para><b>Looks</b>: RRS reads each pack's on/off and sliders from its MelonPreferences every
/// frame, so a chart's look is written there and your own values are put back afterwards.</para>
/// <para><b>Signals</b>: events through <c>RRS.Fire</c> on <c>bossbattles.*</c> channels, and floats
/// as <c>_RRS_bossbattles_*</c> shader globals (RRS's own <c>SetGlobal</c> publishes names no shader
/// can declare). See docs/chart-format.md.</para>
/// </summary>
internal sealed class ReShade
{
    private static bool _searched;
    private static MethodInfo? _fire;
    private static PropertyInfo? _ready;
    private static PropertyInfo? _packs;

    private readonly Dictionary<object, Original> _originals = [];
    private readonly HashSet<string> _reported = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How strongly looks and signals apply: 0 off, 1 full.</summary>
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

    /// <summary>A one-shot event on the "bossbattles.<paramref name="name"/>" channel.</summary>
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

    /// <summary>A float any shader can read as <c>_RRS_bossbattles_name</c>; state, so never scaled.</summary>
    public static void SetGlobal(string name, float value) => Shader.SetGlobalFloat($"_RRS_bossbattles_{name}", value);

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
                    Set(pack.Enabled, original.Enabled.Value);
                    foreach ((MelonPreferences_Entry<float> entry, var value) in original.Values) Set(entry, value);
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

                    // Strength scales the chart's change from your value, not the value itself.
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
    /// Loads the chart's packs during the countdown: RRS loads a pack synchronously the first frame
    /// it is on, a hitch mid-battle. Once loaded a pack stays loaded, so later switches cost nothing.
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
                Logger.Write(loaded ? LogLevel.Debug : LogLevel.Error, $"[{(loaded ? "Visuals.Preloaded" : "Visuals.PreloadFailed")}] {pack.Name}: {(loaded ? "loaded" : "did not load")} in {clock.Elapsed.TotalMilliseconds:0.0} ms");
            }
            catch (Exception e)
            {
                Once("Visuals.PreloadFailed", e.InnerException?.Message ?? e.Message);
            }
        }
    }

    /// <summary>Puts back every pack setting a chart changed.</summary>
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

        Logger.Debug($"[Visuals.Restored] {_originals.Count} pack(s) put back to your settings");
        _originals.Clear();
    }

    /// <summary>The installed packs and their parameters, for the Logger.</summary>
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
        Logger.Debug($"[Visuals.Pack] {pack.Name}: taken over for the run (yours: {(original.Enabled.Value ? "on" : "off")})");
        return original;
    }

    /// <summary>Writes only a real change: each write raises the entry's changed event.</summary>
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
        if (_reported.Add(key)) Logger.Write(key.Contains("Failed", StringComparison.Ordinal) ? LogLevel.Error : LogLevel.Warning, $"[{key.Split(':')[0]}] {text}");
    }

    private static Pack? FindPack(string name) =>
        Packs().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? Packs().FirstOrDefault(p => string.Equals(p.Id, name, StringComparison.OrdinalIgnoreCase));

    private static List<Pack>? _packCache;

    /// <summary>Reads the pack list again: RRS's "Reload packs" can change it between runs.</summary>
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
                Logger.Debug(rrs == null ? "[Visuals.NoReShade] RumbleReShaded is not installed - chart looks and battle signals are off" : "[Visuals.NoReShade] this RumbleReShaded has no API (1.2.0+ needed)");
                return;
            }

            _fire = api.GetMethod("Fire", new[] { typeof(string), typeof(Vector3), typeof(float), typeof(float), typeof(float) });
            _ready = api.GetProperty("Ready");
            _packs = mod.GetProperty("Packs");
            Logger.Debug($"[Visuals.ReShade] RumbleReShaded {rrs!.GetName().Version} found (API {api.GetProperty("ApiVersion")?.GetValue(null)})");
        }
        catch (Exception e)
        {
            _fire = null;
            Logger.Info($"[Visuals.NoReShade] RumbleReShaded could not be read: {e.Message}");
        }
    }

    private sealed record Pack(object Handle, string Name, string Id, MelonPreferences_Entry<bool> Enabled, List<Param> Params)
    {
        /// <summary>A parameter by label or shader property, ignoring case, spaces and underscores.</summary>
        public Param? Find(string key)
        {
            var wanted = StringHelpers.Squash(key);
            return Params.FirstOrDefault(p => StringHelpers.Squash(p.Label) == wanted || StringHelpers.Squash(p.Property) == wanted);
        }
    }

    private sealed record Param(string Property, string Label, float Min, float Max, MelonPreferences_Entry<float> Entry);

    private sealed record Original((MelonPreferences_Entry<bool> Entry, bool Value) Enabled, Dictionary<MelonPreferences_Entry<float>, float> Values);
}
