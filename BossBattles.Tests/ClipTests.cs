using System.Numerics;
using BossBattles.DevTools.Testing;
using BossBattles.Services.Clips;
using Xunit;

namespace BossBattles.Tests;

public sealed class ClipTests
{
    private static ClipPose At(float x, float yawDegrees = 0f) =>
        new(new Vector3(x, 1.6f, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, yawDegrees * MathF.PI / 180f));

    private static ClipFrame Frame(float t, float x, float yaw = 0f) =>
        new(t, At(x, yaw), At(x - 0.2f), At(x + 0.2f));

    private static Clip Sample() => new("swing", new[]
    {
        Frame(0f, 0f, 0f),
        Frame(1f, 2f, 90f),
        Frame(2f, 4f, 90f),
    });

    [Fact]
    public void PosesInterpolateBetweenFrames()
    {
        ClipFrame mid = Sample().Sample(0.5f);

        Assert.Equal(1f, mid.Head.Position.X, 4);
        var yaw = 2f * MathF.Atan2(mid.Head.Rotation.Y, mid.Head.Rotation.W) * 180f / MathF.PI;
        Assert.Equal(45f, yaw, 2);
    }

    [Fact]
    public void SamplingClampsToTheEnds()
    {
        Clip clip = Sample();

        Assert.Equal(0f, clip.Sample(-1f).Head.Position.X);
        Assert.Equal(4f, clip.Sample(99f).Head.Position.X);
        Assert.Equal(2f, clip.Duration);
    }

    [Fact]
    public void AClipSurvivesTheFile()
    {
        Clip clip = Sample();

        (Clip? read, var error) = ClipFile.Read(ClipFile.Write(clip), "fallback");

        Assert.Null(error);
        Assert.Equal("swing", read!.Name);
        Assert.Equal(clip.Frames.Count, read.Frames.Count);
        Assert.Equal(clip.Frames[1].Head.Position, read.Frames[1].Head.Position);
        Assert.True(Quaternion.Dot(clip.Frames[1].Head.Rotation, read.Frames[1].Head.Rotation) > 0.9999f);
    }

    [Fact]
    public void TheSyntheticSwayHasAFrameEveryThirtiethOfASecond()
    {
        Clip sway = SyntheticClips.Sway("sway", 1.5f);

        Assert.Equal(46, sway.Frames.Count);
        Assert.Equal(1.5f, sway.Duration, 3);
    }

    [Fact]
    public void FingersInterpolateAndSurviveTheFile()
    {
        var open = new ClipFingers(0f, 0f, 0f, 0f);
        var fist = new ClipFingers(1f, 1f, 1f, 0.5f);
        var clip = new Clip("fingers", new[]
        {
            Frame(0f, 0f) with { LeftFingers = open, RightFingers = fist },
            Frame(1f, 0f) with { LeftFingers = fist, RightFingers = fist },
        });

        Assert.Equal(new ClipFingers(0.5f, 0.5f, 0.5f, 0.25f), clip.Sample(0.5f).LeftFingers);

        (Clip? read, _) = ClipFile.Read(ClipFile.Write(clip), "x");
        Assert.Equal(fist, read!.Frames[0].RightFingers);
        Assert.Null(ClipFile.Read(ClipFile.Write(Sample()), "x").Clip!.Frames[0].LeftFingers);
    }

    [Fact]
    public void BrokenClipsSayWhy()
    {
        Assert.StartsWith("not valid clip JSON", ClipFile.Read("{", "x").Error, StringComparison.Ordinal);
        Assert.Equal("the clip has no frames", ClipFile.Read(/*lang=json,strict*/ """{ "frames": [] }""", "x").Error);
        Assert.Equal("frames[0] needs h, l and r as [px, py, pz, qx, qy, qz, qw]",
            ClipFile.Read(/*lang=json,strict*/ """{ "frames": [ { "t": 0, "h": [1, 2] } ] }""", "x").Error);
    }

    [Fact]
    public void TheLibraryFindsClipsByNameOrFileAndNeverOverwrites()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"BossBattles-{Guid.NewGuid():N}");
        try
        {
            var library = new ClipLibrary(folder);
            var first = library.Save(Sample());
            var second = library.Save(Sample());

            Assert.NotEqual(first, second);

            library.Reload();
            Assert.NotNull(library.Find("swing"));
            Assert.NotNull(library.Find("swing-2"));
            Assert.Empty(library.Errors);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
