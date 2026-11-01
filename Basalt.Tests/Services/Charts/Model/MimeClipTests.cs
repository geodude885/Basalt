using System.Numerics;
using Basalt.Services.Charts;
using Basalt.Services.Charts.Files;
using Basalt.Services.Clips;
using Xunit;

namespace Basalt.Tests.Services.Charts.Model;

public class MimeClipTests
{
    [Fact]
    public void AMimedClipIsReadAndPlayedMimed()
    {
        var chart = ChartLoader.Load(/*lang=json,strict*/ """
            { "format": 2, "performances": [ { "clips": [ { "t": 0, "clip": "a" }, { "t": 5, "clip": "b", "mime": true } ] } ] }
            """).Chart!;

        Assert.False(chart.Performances[0].Clips[0].Mime);
        Assert.True(chart.Performances[0].Clips[1].Mime);

        var plays = new ChartRunner(chart).Advance(10).OfType<PlayClip>().ToList();
        Assert.False(plays[0].Mime);
        Assert.True(plays[1].Mime);
    }

    [Fact]
    public void KeepingAMimedClipWritesItMimed()
    {
        var chart = ChartLoader.Load(/*lang=json,strict*/ """{ "actors": [ { "id": "boss" } ] }""").Chart!;
        var pose = new ClipPose(Vector3.Zero, Quaternion.Identity);
        ClipFrame Frame(float t)
        {
            return new(t, pose, pose, pose);
        }

        var clip = new Clip("boss-clip-1", new[] { Frame(0), Frame(4) });

        var whole = ChartWriter.WritePerformances(/*lang=json,strict*/ """{ "actors": [ { "id": "boss" } ] }""", chart, "boss", clip, 0, mime: true);
        Assert.True(ChartLoader.Load(whole).Chart!.Performances[0].Clips[0].Mime);
    }
}
