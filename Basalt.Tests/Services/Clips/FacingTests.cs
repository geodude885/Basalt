using System.Numerics;
using Basalt.Services;
using Basalt.Services.Charts;
using Basalt.Services.Clips;
using Xunit;

namespace Basalt.Tests.Services.Clips;

public class FacingTests
{
    [Theory]
    [InlineData(350f, 10f, 0.5f, 0f)]
    [InlineData(10f, 350f, 0.5f, 0f)]
    [InlineData(90f, 180f, 0.5f, 135f)]
    [InlineData(0f, 270f, 0.5f, 315f)]
    public void FacingsInterpolateTheShortWayRound(float from, float to, float t, float expected) =>
        Assert.Equal(expected, AngleHelpers.Lerp(from, to, t) % 360f, 2);

    [Fact]
    public void AClipKeepsItsFacingsOnDisk()
    {
        var pose = new ClipPose(Vector3.Zero, Quaternion.Identity);
        var clip = new Clip("t", new[]
        {
            new ClipFrame(0f, pose, pose, pose, Vector2.Zero, 350f),
            new ClipFrame(1f, pose, pose, pose, Vector2.Zero, 30f),
        });

        (var read, _) = ClipFile.Read(ClipFile.Write(clip), "t");

        Assert.Equal(350f, read!.Frames[0].Yaw);
        Assert.Equal(10f, read.GetClipFrameAt(0.5f).Yaw!.Value, 2);
    }

    [Fact]
    public void LaneYawsRoundTrip()
    {
        var lane = Anchor.CreateFacingDirection(Vector3.Zero, new Vector3(1f, 0f, 0f));

        Assert.Equal(90f, lane.Yaw, 2);
        Assert.Equal(180f, lane.ToLocalYaw(270f), 2);
        Assert.Equal(270f, lane.ToWorldYaw(180f), 2);
    }
}
