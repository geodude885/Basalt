using System.Numerics;
using Basalt.Services.Charts;
using Xunit;

namespace Basalt.Tests;

public class AnchorTests
{
    private const float Tolerance = 1e-4f;

    [Fact]
    public void LaneAxesFollowThePlayersFacing()
    {
        // Facing +X: down the lane is +X, lane-right is -Z (Unity is left-handed).
        var frame = Anchor.CreateFacingDirection(new Vector3(10, 0, 5), new Vector3(1, 0.3f, 0));

        AssertClose(new Vector3(1, 0, 0), frame.Forward);
        AssertClose(new Vector3(0, 0, -1), frame.Right);
        AssertClose(new Vector3(22, 1.5f, 4), frame.ToWorld(new Vector3(1, 1.5f, 12)));
    }

    [Fact]
    public void ToLocalUndoesToWorld()
    {
        var frame = Anchor.CreateFacingDirection(new Vector3(-3, 0.7f, 8), new Vector3(0.6f, 0, -0.8f));
        var lane = new Vector3(-1.2f, 1.4f, 10.5f);

        AssertClose(lane, frame.ToLocal(frame.ToWorld(lane)));
    }

    [Fact]
    public void AnActorWithNoFacingLooksBackDownTheLane()
    {
        var north = Anchor.CreateFacingDirection(Vector3.Zero, Vector3.UnitZ);

        Assert.Equal(180f, north.ActorYaw(0), 3);
        Assert.Equal(210f, north.ActorYaw(30), 3);
        Assert.Equal(150f, north.ActorYaw(-30), 3);
    }

    private static void AssertClose(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < Tolerance, $"expected {expected}, got {actual}");
}

public class BallisticsTests
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

        var velocity = Ballistics.GetLaunchVelocity(from, to, speed, Gravity);
        var time = Vector3.Distance(from, to) / speed;

        var landed = from + (velocity * time) - (Vector3.UnitY * (0.5f * Gravity * time * time));
        Assert.True(Vector3.Distance(to, landed) < 1e-3f, $"landed at {landed}, aimed at {to}");
    }

    [Fact]
    public void ASlowShotIsAimedHigherThanAFastOne()
    {
        var from = new Vector3(0, 1.4f, 10);
        var to = new Vector3(0, 1.4f, 0);

        Assert.True(Ballistics.GetLaunchVelocity(from, to, 6f, Gravity).Y > Ballistics.GetLaunchVelocity(from, to, 20f, Gravity).Y);
    }

    [Fact]
    public void ZeroSpeedIsRejected() => Assert.Throws<ArgumentOutOfRangeException>(() => Ballistics.GetLaunchVelocity(Vector3.Zero, Vector3.UnitZ, 0f, Gravity));
}
