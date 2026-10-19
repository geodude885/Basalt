namespace Basalt.Services.Charts;

/// <summary>
/// A RumbleReShaded pack turned on at <see cref="Time"/> with the values in <see cref="Set"/>,
/// fading over <see cref="Fade"/> seconds; or, with <see cref="Off"/>, faded back to the player's own.
/// </summary>
public record VisualCue(double Time, string Pack, IReadOnlyDictionary<string, float> Set, bool Off, double Fade);

/// <summary>Where one pack stands at a moment: on or not, and the charted parameters' values.</summary>
public record PackLook(bool On, IReadOnlyDictionary<string, float> Values);

/// <summary>
/// Every pack's look at a chart time, stateless so a run started part way looks as it would have.
/// <c>baseline</c> gives the player's own value, which cues fade from and "off" fades back to.
/// </summary>
public static class LookTimeline
{
    public static Dictionary<string, PackLook> Evaluate(IReadOnlyList<VisualCue> cues, double time, Func<string, string, float> baseline)
    {
        var looks = new Dictionary<string, PackLook>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in cues.Where(c => c.Time <= time).GroupBy(c => c.Pack, StringComparer.OrdinalIgnoreCase))
        {
            var packCues = pack.OrderBy(c => c.Time).ToList();
            var values = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            var on = false;

            // Each cue fades from the look so far, frozen where the next cue cut in.
            foreach ((var cue, var cueEnd) in packCues.Zip(packCues.Skip(1).Select(c => c.Time).Append(time)))
            {
                var k = cue.Fade <= 0 ? 1 : Math.Clamp((cueEnd - cue.Time) / cue.Fade, 0, 1);

                var targets = cue.Off
                    ? values.Keys.ToDictionary(key => key, key => baseline(pack.Key, key), StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, float>(cue.Set, StringComparer.OrdinalIgnoreCase);

                foreach ((var key, var target) in targets)
                {
                    var from = values.TryGetValue(key, out var v) ? v : baseline(pack.Key, key);
                    values[key] = (float)(from + ((target - from) * k));
                }

                // On at once; off only once the fade back is over.
                on = !cue.Off || k < 1;
                if (cue.Off && k >= 1) values.Clear();
            }

            looks[pack.Key] = new PackLook(on, values);
        }

        return looks;
    }
}
