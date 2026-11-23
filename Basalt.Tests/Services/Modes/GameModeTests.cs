using Basalt.Services.Modes;
using Xunit;

namespace Basalt.Tests.Services.Modes;

public class GameModeTests
{
    [Fact]
    public void NormalIsTheFirstMode() => Assert.Equal(GameMode.Normal, GameModes.All[0]);

    [Theory]
    [InlineData(GameMode.Normal, true)]
    [InlineData(GameMode.Practice, false)]
    [InlineData(GameMode.Hardcore, true)]
    public void OnlyPracticeSkipsSavingScores(GameMode mode, bool saves) => Assert.Equal(saves, mode.SavesScores());

    [Theory]
    [InlineData(GameMode.Normal, false)]
    [InlineData(GameMode.Practice, true)]
    [InlineData(GameMode.Hardcore, false)]
    public void OnlyPracticeKeepsYouAlive(GameMode mode, bool invincible) => Assert.Equal(invincible, mode.PlayerInvincible());

    [Theory]
    [InlineData(GameMode.Normal, false)]
    [InlineData(GameMode.Practice, false)]
    [InlineData(GameMode.Hardcore, true)]
    public void OnlyHardcoreTakesFullStructureDamage(GameMode mode, bool full) => Assert.Equal(full, mode.FullStructureDamage());

    [Fact]
    public void EveryModeIsDescribed() =>
        Assert.Equal(GameModes.All.Count, GameModes.All.Select(m => m.Describe()).Distinct().Count());
}
