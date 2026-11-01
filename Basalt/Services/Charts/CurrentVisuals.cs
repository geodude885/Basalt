using Basalt.Services.Charts.Model;

namespace Basalt.Services.Charts;

public record PackLook(bool On, IReadOnlyDictionary<string, float> Values);

public static class CurrentVisuals
{
    public static Dictionary<string, PackLook> Evaluate(IReadOnlyList<ShaderCue> cues, double time, Func<string, string, float> baseline)
    {
        var looks = new Dictionary<string, PackLook>(StringComparer.OrdinalIgnoreCase);

        foreach (var pack in cues.Where(c => c.Time <= time).GroupBy(c => c.Pack, StringComparer.OrdinalIgnoreCase))
        {
            var packCues = pack.OrderBy(c => c.Time).ToList();
            var values = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            var on = false;

            // Either get value based of fading between cue changes, or stay frozen
            foreach ((var cue, var cueEnd) in packCues.Zip(packCues.Skip(1).Select(c => c.Time).Append(time)))
            {
                var k = cue.Fade <= 0 ? 1 : Math.Clamp((cueEnd - cue.Time) / cue.Fade, 0, 1);

                var targets = cue.Off
                    ? values.Keys.ToDictionary(key => key, key => baseline(pack.Key, key), StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, float>(cue.Values, StringComparer.OrdinalIgnoreCase);

                foreach ((var key, var target) in targets)
                {
                    var from = values.TryGetValue(key, out var v) ? v : baseline(pack.Key, key);
                    values[key] = (float)(from + ((target - from) * k));
                }

                on = !cue.Off || k < 1;
                if (cue.Off && k >= 1) values.Clear();
            }

            looks[pack.Key] = new PackLook(on, values);
        }

        return looks;
    }
}
