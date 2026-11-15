using Basalt.Services.Charts.Files;
using Basalt.Services.Scoring;

namespace Basalt.Services.Help;

public record HelpTopic(string Title, string Text, Func<double, string>? TextAt = null);

public static class PlayerGuide
{
    public static IReadOnlyList<HelpTopic> Topics { get; } =
    [
        new("Score",
            $"Surviving to the end gives you the full amount of points from completing a chart. Losing will reduce this, depending on how far though the chart you were.\n" +
            $"Each point of damage you do deal will add points, and each point of damage you take will remove some."),
        new("Ranks", RanksText(0), RanksText),
        new("Chart rules",
            "Charts may have different rulesets; such as only allowing spawning of discs, no structures etc.\n" +
            $"Some charts will also break stationary structures after a few seconds, and some silently break structures that go past you."),
        new("Settings",
            "In Basalt's mod settings:\n" +
            "Flashing: full, reduced or off (only if using RumbleReShaded)\n" +
            "Shader Intensity: chart effects intensity (only if using RumbleReShaded)\n" +
            "Phase Banners: show phase names (defaults OFF).\n" +
            "Menu here: bring this menu to you. In the Gym it will stay in that spot until moved again."),
        new("Adding charts",
            $"Put chart packages ({ChartPackage.Extension}) in UserData/Basalt/Charts. They should appear without restarting the game."),
    ];

    private static string RanksText(double time)
    {
        var limits = new List<string> { $"<b>{RankLook.Markup(ScoreKeeper.Hitless, time, sized: false)}</b> none" };
        var from = 1;
        foreach ((var rank, var max) in ScoreKeeper.Ranks)
        {
            var range = max == int.MaxValue ? $"{from}+" : from == max ? $"{max}" : $"{from}-{max}";
            limits.Add($"<b>{RankLook.Markup(rank, time, sized: false)}</b> {range}");
            from = max + 1;
        }

        return "Your rank is set by the damage you take:\n" + string.Join(",  ", limits) + "\n" +
               $"Damage you deal makes up for some of it, but only lifts you as far as {ScoreKeeper.DealingDamageCanRaiseToRank}.\n";
    }
}
