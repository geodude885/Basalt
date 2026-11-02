using Basalt.Services.Scoring;
using Xunit;

namespace Basalt.Tests.Services.Scoring;

public class ScoreKeeperTests
{
    [Fact]
    public void ACleanSurvivalIsAnSSPlus()
    {
        var score = new ScoreKeeper("Warm-up");

        var result = score.Finish(survived: true, seconds: 28, battleLength: 28);

        Assert.Equal("SS+", result.Rank);
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
        Assert.Equal("SS", result.Rank);
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
    [InlineData(0, "SS+")]
    [InlineData(1, "SS")]
    [InlineData(2, "SS")]
    [InlineData(3, "S")]
    [InlineData(4, "S")]
    [InlineData(5, "A+")]
    [InlineData(6, "A+")]
    [InlineData(7, "A")]
    [InlineData(10, "A")]
    [InlineData(11, "B+")]
    [InlineData(13, "B+")]
    [InlineData(14, "B")]
    [InlineData(16, "B")]
    [InlineData(17, "C+")]
    [InlineData(18, "C+")]
    [InlineData(19, "C")]
    [InlineData(500, "C")]
    public void RankIsAboutStayingClean(int damage, string rank) =>
        Assert.Equal(rank, ScoreKeeper.Rank(true, damage));

    [Theory]
    [InlineData(0.25, "D")]
    [InlineData(0.5, "D")]
    [InlineData(0.75, "D+")]
    public void ADefeatPastHalfwayIsADPlus(double lasted, string rank) =>
        Assert.Equal(rank, ScoreKeeper.Rank(false, 0, lasted: lasted));

    [Theory]
    [InlineData(0, 0, "SS+")]
    [InlineData(16, 0, "B")]
    [InlineData(16, 10, "B+")]
    [InlineData(16, 15, "A")]
    [InlineData(16, 500, "A")]
    [InlineData(4, 500, "S")]
    public void DamageDealtBuysBackDamageTakenUpToAnA(int taken, double dealt, string rank) =>
        Assert.Equal(rank, ScoreKeeper.Rank(true, taken, dealt));

    [Theory]
    [InlineData(20, 0, "D")]
    [InlineData(20, 4, "D")]
    [InlineData(20, 5, "C+")]
    [InlineData(20, 10, "B")]
    [InlineData(20, 25, "A")]
    [InlineData(20, 500, "A")]
    [InlineData(1, 500, "A")]
    public void DamageDealtLiftsADefeatUpToAnA(int taken, double dealt, string rank) =>
        Assert.Equal(rank, ScoreKeeper.Rank(false, taken, dealt, lasted: 0.1));
}
