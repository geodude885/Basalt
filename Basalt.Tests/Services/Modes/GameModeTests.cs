using Basalt.Services.Modes;
using Xunit;

namespace Basalt.Tests.Services.Modes;

public class GameModeTests
{
    [Fact]
    public void NormalIsTheFirstMode() => Assert.Equal(GameMode.Normal, GameModes.All[0]);

    [Fact]
    public void OnlyNormalSavesScores()
    {
        Assert.True(GameMode.Normal.SavesScores());
        Assert.False(GameMode.Practice.SavesScores());
    }

    [Fact]
    public void OnlyPracticeKeepsYouAlive()
    {
        Assert.False(GameMode.Normal.PlayerInvincible());
        Assert.True(GameMode.Practice.PlayerInvincible());
    }

    [Fact]
    public void EveryModeIsDescribed() =>
        Assert.Equal(GameModes.All.Count, GameModes.All.Select(m => m.Describe()).Distinct().Count());
}
