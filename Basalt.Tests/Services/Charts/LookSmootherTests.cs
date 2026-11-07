using Basalt.Services.Charts;
using Xunit;

namespace Basalt.Tests.Services.Charts;

public class LookSmootherTests
{
    private const float Frame = 1f / 90f;

    [Fact]
    public void FullFollowsTheChartAtOnce()
    {
        var smoother = new VisualFlashingReducer();

        Assert.Equal(2f, smoother.WithFlashingReduction("Pack", "Fog", 2f, Frame, start: 0f));
        Assert.Equal(0f, smoother.WithFlashingReduction("Pack", "Fog", 0f, Frame, start: 0f));
    }

    [Theory]
    // An on-off pulse every p seconds keeps tanh(p / 4delay) of its swing.
    [InlineData(0.2f, 0.3, 0.4f)]
    [InlineData(0.75f, 0.3, 0.12f)]
    [InlineData(0.75f, 0.2, 0.1f)]
    public void FastPulsesAreFlattened(float delay, double period, float mostOfTheSwing)
    {
        // Jouou at 112 s: an instant change every `period`, back again half way through.
        var swing = PeakToPeak(new VisualFlashingReducer { Delay = delay }, period, seconds: 5);

        Assert.True(swing < mostOfTheSwing, $"a 0-1 pulse every {period}s still swung {swing:0.00} with a {delay}s delay");
    }

    [Fact]
    public void SlowPulsesComeThroughWhenReduced()
    {
        var swing = PeakToPeak(new VisualFlashingReducer { Delay = 0.2f }, period: 1.2, seconds: 6);

        Assert.True(swing > 0.88f, $"a 0-1 pulse every 1.2s swung only {swing:0.00}");
    }

    [Fact]
    public void AOneOffCutBecomesAQuickFadeAndLandsExactly()
    {
        var smoother = new VisualFlashingReducer { Delay = 0.2f };
        smoother.WithFlashingReduction("Pack", "Fog", 0f, Frame, start: 0f);

        var after = Enumerable.Range(0, 180).Select(_ => smoother.WithFlashingReduction("Pack", "Fog", 1f, Frame, start: 0f)).ToArray();

        Assert.InRange(after[0], 0.01f, 0.2f);
        Assert.Equal(1f, after[^1], 2);
    }

    [Fact]
    public void AfterABurstItSettlesOnTheChartsLook()
    {
        var smoother = new VisualFlashingReducer { Delay = 0.75f };
        PeakToPeak(smoother, period: 0.2, seconds: 3);

        var shown = 0f;
        for (var t = 0f; t < 5f; t += Frame) shown = smoother.WithFlashingReduction("Pack", "Fog", 0.4f, Frame, start: 0f);

        Assert.Equal(0.4f, shown, 2);
    }

    [Fact]
    public void AValueTheChartHasNotSetYetStartsFromThePlayersOwn()
    {
        var smoother = new VisualFlashingReducer { Delay = 0.75f };

        Assert.InRange(smoother.WithFlashingReduction("Pack", "Fog", 2f, Frame, start: 1f), 1f, 1.05f);
    }

    // The 0-1 square wave of the chart's value, and how far what is shown swings once it has settled into it.
    private static float PeakToPeak(VisualFlashingReducer smoother, double period, double seconds)
    {
        float low = float.MaxValue, high = float.MinValue;
        for (var t = 0.0; t < seconds; t += Frame)
        {
            var target = t % period < period / 2 ? 1f : 0f;
            var shown = smoother.WithFlashingReduction("Pack", "Fog", target, Frame, start: 0f);
            if (t < seconds / 2) continue;

            low = Math.Min(low, shown);
            high = Math.Max(high, shown);
        }

        return high - low;
    }
}
