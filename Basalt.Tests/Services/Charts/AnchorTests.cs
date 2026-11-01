using System.Numerics;
using Basalt.Services.Charts;
using Xunit;

namespace Basalt.Tests.Services.Charts;

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
