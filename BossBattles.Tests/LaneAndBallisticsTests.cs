using System.Numerics;
using BossBattles.Services.Charts;
using Xunit;

namespace BossBattles.Tests;

public sealed class LaneFrameTests
{
    private const float Tolerance = 1e-4f;

    [Fact]
    public void LaneAxesFollowYourFacing()
    {
        // Facing +X: down the lane is +X, lane-right is -Z (Unity is left-handed).
        var frame = LaneFrame.From(new Vector3(10, 0, 5), new Vector3(1, 0.3f, 0));

        AssertClose(new Vector3(1, 0, 0), frame.Anchor.Forward);
        AssertClose(new Vector3(0, 0, -1), frame.Anchor.Right);
        AssertClose(new Vector3(22, 1.5f, 4), frame.ToWorld(new Vector3(1, 1.5f, 12)));
    }

    [Fact]
    public void ToLaneUndoesToWorld()
    {
        var frame = LaneFrame.From(new Vector3(-3, 0.7f, 8), new Vector3(0.6f, 0, -0.8f), 0.5f);
        var lane = new Vector3(-1.2f, 1.4f, 10.5f);

        AssertClose(lane, frame.ToLane(frame.ToWorld(lane)));
    }

    [Fact]
    public void BetweenFitsTheChartLaneToTheRealOne()
    {
        // A 12 m chart lane fitted between two arena spots 6 m apart: z is halved, x and y are not.
        var frame = LaneFrame.Between(new Vector3(0, 0, 0), new Vector3(0, 0.5f, 6), 12f);

        Assert.Equal(0.5f, frame.DepthScale, 4);
        AssertClose(new Vector3(0, 0, 6), frame.ToWorld(new Vector3(0, 0, 12)));
        AssertClose(new Vector3(1, 1.4f, 3), frame.ToWorld(new Vector3(1, 1.4f, 6)));
    }

    [Fact]
    public void AnActorWithNoFacingLooksBackDownTheLane()
    {
        var north = LaneFrame.From(Vector3.Zero, Vector3.UnitZ);

        Assert.Equal(180f, north.ActorYawDegrees(0), 3);
        Assert.Equal(210f, north.ActorYawDegrees(30), 3);
        Assert.Equal(150f, north.ActorYawDegrees(-30), 3);
    }

    private static void AssertClose(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < Tolerance, $"expected {expected}, got {actual}");
}

public sealed class BallisticsTests
{
    private const float Gravity = 9.81f;

    [Theory]
    [InlineData(8f)]
    [InlineData(14f)]
    [InlineData(30f)]
    public void ALaunchArrivesAtItsTargetAfterDistanceOverSpeed(float speed)
    {
        var from = new Vector3(0, 1.4f, 10);
        var to = new Vector3(0.8f, 1.2f, 0);

        Vector3 velocity = Ballistics.LaunchVelocity(from, to, speed, Gravity);
        var time = Vector3.Distance(from, to) / speed;

        Vector3 landed = from + (velocity * time) - (Vector3.UnitY * (0.5f * Gravity * time * time));
        Assert.True(Vector3.Distance(to, landed) < 1e-3f, $"landed at {landed}, aimed at {to}");
    }

    [Fact]
    public void ASlowShotIsAimedHigherThanAFastOne()
    {
        var from = new Vector3(0, 1.4f, 10);
        var to = new Vector3(0, 1.4f, 0);

        Assert.True(Ballistics.LaunchVelocity(from, to, 6f, Gravity).Y > Ballistics.LaunchVelocity(from, to, 20f, Gravity).Y);
    }

    [Fact]
    public void ZeroSpeedIsRejected() => Assert.Throws<ArgumentOutOfRangeException>(() => Ballistics.LaunchVelocity(Vector3.Zero, Vector3.UnitZ, 0f, Gravity));
}
