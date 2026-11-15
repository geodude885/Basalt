using Basalt.Services.Help;
using Basalt.Services.Scoring;
using Xunit;

namespace Basalt.Tests.Services.Help;

public class PlayerGuideTests
{
    private static string Topic(string title) => PlayerGuide.Topics.Single(t => t.Title == title).Text;

    [Fact]
    public void TopicTitlesAreShortAndDistinct()
    {
        Assert.All(PlayerGuide.Topics, t => Assert.InRange(t.Title.Length, 1, 14));
        Assert.Equal(PlayerGuide.Topics.Count, PlayerGuide.Topics.Select(t => t.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void RanksTopicListsEveryRankWithItsDamage()
    {
        var text = Topic("Ranks");
        Assert.Contains($"<b>{RankLook.Markup("SS+", sized: false)}</b> none", text);
        Assert.Contains($"<b>{RankLook.Markup("SS", sized: false)}</b> 1-2", text);
        Assert.Contains("<b>A</b> 7-10", text);
        Assert.Contains("<b>C+</b> 17-18", text);
        Assert.Contains("<b>C</b> 19+", text);
        Assert.Contains($"as far as {ScoreKeeper.DealingDamageCanRaiseToRank}.", text);
    }

    [Fact]
    public void RanksTopicShimmersOverTime()
    {
        var ranks = PlayerGuide.Topics.Single(t => t.Title == "Ranks");
        Assert.NotNull(ranks.TextAt);
        Assert.Equal(ranks.Text, ranks.TextAt!(0));
        Assert.NotEqual(ranks.TextAt(0), ranks.TextAt(0.5));
    }
}
