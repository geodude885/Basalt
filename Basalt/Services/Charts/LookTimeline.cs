using Basalt.Services.Charts.Model;

namespace Basalt.Services.Charts;

public sealed class LookTimeline
{
    private readonly List<PackTimeline> _packs = [];
    private readonly Dictionary<string, PackLook> _looks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Pack, string Key), float> _baselines = new(PackKeyComparer.Instance);
    private readonly Func<string, string, float> _baseline;

    public LookTimeline(IReadOnlyList<ShaderCue> cues, Func<string, string, float> baseline)
    {
        _baseline = baseline;

        foreach (var pack in cues.GroupBy(c => c.Pack, StringComparer.OrdinalIgnoreCase))
        {
            var packCues = pack.OrderBy(c => c.Time).ToArray();
            var starts = new Dictionary<string, float>[packCues.Length];
            var values = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < packCues.Length; i++)
            {
                starts[i] = new Dictionary<string, float>(values, StringComparer.OrdinalIgnoreCase);
                if (i + 1 < packCues.Length) ApplyCue(pack.Key, packCues[i], packCues[i + 1].Time, values);
            }

            _packs.Add(new PackTimeline(pack.Key, packCues, packCues.Select(c => c.Time).ToArray(), starts));
        }
    }

    public float PlayerDefaultParameters(string pack, string key)
    {
        if (!_baselines.TryGetValue((pack, key), out var value)) _baselines[(pack, key)] = value = _baseline(pack, key);
        return value;
    }

    public IReadOnlyDictionary<string, PackLook> VisualsAt(double time)
    {
        _looks.Clear();

        foreach (var pack in _packs)
        {
            var i = LastAtOrBefore(pack.Times, time);
            if (i < 0) continue;

            pack.Now.Clear();
            foreach ((var key, var value) in pack.Starts[i]) pack.Now[key] = value;

            var on = ApplyCue(pack.Name, pack.Cues[i], time, pack.Now);
            _looks[pack.Name] = new PackLook(on, pack.Now);
        }

        return _looks;
    }

    private bool ApplyCue(string pack, ShaderCue cue, double until, Dictionary<string, float> values)
    {
        var k = cue.Fade <= 0 || cue.Time <= 0 ? 1 : Math.Clamp((until - cue.Time) / cue.Fade, 0, 1);

        if (cue.Off)
        {
            foreach (var key in values.Keys.ToArray()) values[key] = (float)(values[key] + ((PlayerDefaultParameters(pack, key) - values[key]) * k));
            if (k >= 1) values.Clear();
            return k < 1;
        }

        foreach ((var key, var target) in cue.Values)
        {
            var from = values.TryGetValue(key, out var v) ? v : PlayerDefaultParameters(pack, key);
            values[key] = (float)(from + ((target - from) * k));
        }

        return true;
    }

    private static int LastAtOrBefore(double[] times, double time)
    {
        int low = 0, high = times.Length;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (times[mid] <= time) low = mid + 1;
            else high = mid;
        }

        return low - 1;
    }

    private sealed record PackTimeline(string Name, ShaderCue[] Cues, double[] Times, Dictionary<string, float>[] Starts)
    {
        public Dictionary<string, float> Now { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class PackKeyComparer : IEqualityComparer<(string Pack, string Key)>
    {
        public static readonly PackKeyComparer Instance = new();

        public bool Equals((string Pack, string Key) a, (string Pack, string Key) b) =>
            StringComparer.OrdinalIgnoreCase.Equals(a.Pack, b.Pack) && StringComparer.OrdinalIgnoreCase.Equals(a.Key, b.Key);

        public int GetHashCode((string Pack, string Key) value) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.Pack), StringComparer.OrdinalIgnoreCase.GetHashCode(value.Key));
    }
}
