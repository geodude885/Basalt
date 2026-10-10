using System.Numerics;
using BossBattles.Services.Clips;
using Xunit;

namespace BossBattles.Tests;

public sealed class ClipTests
{
    private static ClipPose At(float x, float yawDegrees = 0f) =>
        new(new Vector3(x, 1.6f, 0f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, yawDegrees * MathF.PI / 180f));

    private static ClipFrame Frame(float t, float x, float yaw = 0f, float walk = 0f) =>
        new(t, At(0f), At(x, yaw), At(x - 0.2f), At(x + 0.2f), new Vector2(0f, walk), Vector2.Zero);

    private static Clip Sample() => new("swing", new[]
    {
        Frame(0f, 0f, 0f, walk: 1f),
        Frame(1f, 2f, 90f, walk: 0f),
        Frame(2f, 4f, 90f, walk: 0.5f),
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
    public void SticksHoldUntilTheNextFrame()
    {
        Assert.Equal(1f, Sample().Sample(0.99f).Walk.Y);
        Assert.Equal(0f, Sample().Sample(1.01f).Walk.Y);
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
        Assert.Equal(new Vector2(0f, 0.5f), read.Frames[2].Walk);
    }

    [Fact]
    public void TheSyntheticSwayHasAFrameEveryThirtiethOfASecond()
    {
        Clip sway = SyntheticClips.Sway("sway", 1.5f);

        Assert.Equal(46, sway.Frames.Count);
        Assert.Equal(1.5f, sway.Duration, 3);
        Assert.All(sway.Frames, f => Assert.Equal(Vector2.Zero, f.Walk));
    }

    [Fact]
    public void BrokenClipsSayWhy()
    {
        Assert.StartsWith("not valid clip JSON", ClipFile.Read("{", "x").Error, StringComparison.Ordinal);
        Assert.Equal("the clip has no frames", ClipFile.Read(/*lang=json,strict*/ """{ "frames": [] }""", "x").Error);
        Assert.Equal("frames[0] needs o, h, l and r as [px, py, pz, qx, qy, qz, qw]",
            ClipFile.Read(/*lang=json,strict*/ """{ "frames": [ { "t": 0, "o": [1, 2] } ] }""", "x").Error);
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
