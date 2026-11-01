using System.Numerics;
using Basalt.Services.Charts;
using Xunit;

namespace Basalt.Tests.Services.Charts;

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
