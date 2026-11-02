using Basalt.Services.Scoring;
using Xunit;

namespace Basalt.Tests.Services.Scoring;

public class RankLookTests
{
    [Theory]
    [InlineData("A+")]
    [InlineData("D")]
    public void BelowSARankIsPlain(string rank) => Assert.Equal(rank, RankLook.Markup(rank));

    [Theory]
    [InlineData("S", false)]
    [InlineData("SS", true)]
    [InlineData("SS+", true)]
    public void OnlyTheTopRanksMove(string rank, bool moves) =>
        Assert.Equal(moves, RankLook.Markup(rank, 0) != RankLook.Markup(rank, 0.4));

    [Fact]
    public void EachLetterOfSSPlusIsItsOwnColour()
    {
        var markup = RankLook.Markup("SS+");

        Assert.Equal(3, markup.Split("<color=#").Skip(1).Select(c => c[..6]).Distinct().Count());
    }

    [Fact]
    public void AnSIsBlue() => Assert.Contains("<color=#0066FF>", RankLook.Markup("S"));
}
