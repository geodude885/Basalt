namespace BossBattles.Services.Charts;

/// <summary>
/// A change of look at <see cref="Time"/>: a RumbleReShaded shader pack, by name, turned on with
/// the parameter values in <see cref="Set"/> (keys are a parameter's label or property, e.g.
/// "Saturation" or "_Saturation"), fading in over <see cref="Fade"/> seconds - or, with
/// <see cref="Off"/>, faded back to the player's own settings and then put back as it was.
/// </summary>
public sealed record VisualCue(double Time, string Pack, IReadOnlyDictionary<string, float> Set, bool Off, double Fade);

/// <summary>Where one pack stands at a moment: on or not, and the charted parameters' values.</summary>
public sealed record PackLook(bool On, IReadOnlyDictionary<string, float> Values);

/// <summary>
/// Works out every pack's look at a chart time from the cues - stateless, so a run started part
/// way through (a section) looks as it would have got there. Values not charted are left to the
/// player; <c>baseline</c> gives the player's own value of a charted one, which a cue
/// fades from (the first time) and an "off" fades back to.
/// </summary>
public static class LookTimeline
{
    public static Dictionary<string, PackLook> Evaluate(IReadOnlyList<VisualCue> cues, double time, Func<string, string, float> baseline)
    {
        var looks = new Dictionary<string, PackLook>(StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, VisualCue> pack in cues.Where(c => c.Time <= time).GroupBy(c => c.Pack, StringComparer.OrdinalIgnoreCase))
        {
            var mine = pack.OrderBy(c => c.Time).ToList();
            var values = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            var on = false;

            for (var i = 0; i < mine.Count; i++)
            {
                VisualCue cue = mine[i];

                // Where this cue fades from: the look so far, frozen where the next cue cut in.
                var cueEnd = i + 1 < mine.Count ? mine[i + 1].Time : time;
                var k = cue.Fade <= 0 ? 1 : Math.Clamp((cueEnd - cue.Time) / cue.Fade, 0, 1);

                Dictionary<string, float> targets = cue.Off
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
