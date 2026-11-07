using Basalt.Services.Charts;
using Basalt.Services.Charts.Model;
using Xunit;

namespace Basalt.Tests.Services.Charts;

public class LookTimelineTests
{
    private static float Yours(string pack, string key) => key.Length * 0.1f;

    [Fact]
    public void AgreesWithWorkingEveryCueOutFromScratch()
    {
        var cues = ManyCues(400, seed: 7);
        var timeline = new LookTimeline(cues, Yours);

        for (var time = -1.0; time < 230; time += 0.137)
        {
            var expected = FromScratch(cues, time, Yours);
            var actual = timeline.VisualsAt(time);

            Assert.Equal(expected.Count, actual.Count);
            foreach ((var pack, var look) in expected)
            {
                Assert.Equal(look.On, actual[pack].On);
                Assert.Equal(look.Values.Count, actual[pack].Values.Count);
                foreach ((var key, var value) in look.Values) Assert.Equal(value, actual[pack].Values[key], 4);
            }
        }
    }

    [Fact]
    public void CuesAtTheSameTimeApplyInTheirOrder()
    {
        var cues = new[]
        {
            new ShaderCue(1, "Pack", new Dictionary<string, float> { ["A"] = 2f }, false, 0),
            new ShaderCue(1, "Pack", new Dictionary<string, float> { ["A"] = 5f }, false, 0),
        };

        Assert.Equal(5f, new LookTimeline(cues, Yours).VisualsAt(1)["Pack"].Values["A"]);
    }

    [Fact]
    public void FindingALookIsFasterThanWorkingItOutEachFrame()
    {
        var cues = ManyCues(400, seed: 3);
        var timeline = new LookTimeline(cues, Yours);

        var frames = Enumerable.Range(0, 2000).Select(i => i * 0.1).ToArray();
        var kept = System.Diagnostics.Stopwatch.StartNew();
        foreach (var time in frames) timeline.VisualsAt(time);
        kept.Stop();

        var scratch = System.Diagnostics.Stopwatch.StartNew();
        foreach (var time in frames) FromScratch(cues, time, Yours);
        scratch.Stop();

        Assert.True(kept.Elapsed < scratch.Elapsed / 5, $"kept {kept.Elapsed.TotalMilliseconds:0.0} ms, from scratch {scratch.Elapsed.TotalMilliseconds:0.0} ms for {frames.Length} frames");
    }

    // A chart's worth of cues over 220 s: two packs, overlapping fades, offs, and cues sharing a time.
    private static ShaderCue[] ManyCues(int count, int seed)
    {
        var random = new Random(seed);
        string[] keys = ["Fog density", "Lights", "Darkness", "Sky fog", "Glow"];

        return Enumerable.Range(0, count).Select(i =>
        {
            var time = Math.Round(random.NextDouble() * 220, i % 9 == 0 ? 0 : 2);
            var pack = random.Next(4) == 0 ? "Other" : "BossBattles Stage";
            var off = random.Next(25) == 0;
            var values = off ? [] : keys.Where(_ => random.Next(2) == 0).ToDictionary(k => k, _ => (float)random.NextDouble() * 2f);
            return new ShaderCue(time, pack, values, off, random.Next(3) == 0 ? 0 : random.NextDouble() * 2);
        }).ToArray();
    }

    // How looks were worked out before LookTimeline: every cue up to `time`, every frame.
    private static Dictionary<string, PackLook> FromScratch(IReadOnlyList<ShaderCue> cues, double time, Func<string, string, float> baseline)
    {
        var looks = new Dictionary<string, PackLook>(StringComparer.OrdinalIgnoreCase);

        foreach (var pack in cues.Where(c => c.Time <= time).GroupBy(c => c.Pack, StringComparer.OrdinalIgnoreCase))
        {
            var packCues = pack.OrderBy(c => c.Time).ToList();
            var values = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            var on = false;

            foreach ((var cue, var cueEnd) in packCues.Zip(packCues.Skip(1).Select(c => c.Time).Append(time)))
            {
                var k = cue.Fade <= 0 || cue.Time <= 0 ? 1 : Math.Clamp((cueEnd - cue.Time) / cue.Fade, 0, 1);

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
