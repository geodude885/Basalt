using Basalt.Services.Scoring;
using Xunit;

namespace Basalt.Tests.Services.Scoring;

public class ScoreKeeperTests
{
    [Fact]
    public void ACleanSurvivalIsAnS()
    {
        var score = new ScoreKeeper("Warm-up");

        var result = score.Finish(survived: true, seconds: 28, battleLength: 28);

        Assert.Equal("S", result.Rank);
        Assert.Equal(ScoreKeeper.SurvivalPoints, result.Score);
    }

    [Fact]
    public void ActorDamageIsABonusScaledByThePhase()
    {
        var score = new ScoreKeeper("Crossfire");

        score.ActorDamaged(3, multiplier: 1);
        score.ActorDamaged(2, multiplier: 2);
        score.PlayerDamaged(2);

        var result = score.Finish(survived: true, seconds: 33, battleLength: 33);

        Assert.Equal(7, result.ActorDamage, 6);
        Assert.Equal(
            ScoreKeeper.SurvivalPoints + (7 * ScoreKeeper.ActorDamagePoints) - (2 * ScoreKeeper.DamageTakenPoints),
            result.Score);
        Assert.Equal("A", result.Rank);
    }

    [Fact]
    public void NotSurvivingEarnsAShareOfHalfTheSurvivalPoints()
    {
        var score = new ScoreKeeper("Crossfire");
        score.PlayerDamaged(20);

        var result = score.Finish(survived: false, seconds: 10, battleLength: 40);

        Assert.Equal("D", result.Rank);
        Assert.Equal(1, result.HitsTaken);
        Assert.Equal(0, result.Score); // 125 for lasting a quarter, minus 500 for the damage, floored at 0
    }

    [Theory]
    [InlineData(true, 0, "S")]
    [InlineData(true, 3, "A")]
    [InlineData(true, 4, "B")]
    [InlineData(true, 8, "B")]
    [InlineData(true, 9, "C")]
    [InlineData(false, 0, "D")]
    public void RankIsAboutStayingClean(bool survived, int damage, string rank) =>
        Assert.Equal(rank, ScoreKeeper.Rank(survived, damage));
}
