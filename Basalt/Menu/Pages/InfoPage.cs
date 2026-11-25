using Basalt.Services.Charts.Files;
using Basalt.Services.Scoring;

namespace Basalt.Menu.Pages;

internal class InfoPage() : PanelPage("guide")
{
    internal record Topic(string Title, string Text, Func<double, string>? TextAt = null);

    internal static IReadOnlyList<Topic> Topics { get; } =
    [
        new("Setup",
            $"Music: Ensure that Rumble music is enabled in order to hear songs whilst playing.\n" +
            $"Shaders: RumbleReShaded is optional, but strongly recommended. For the best experience, ensure it is enabled in UiFramework, and the shader pack supplied with Basalt is in your shaders folder"),
        new("Score",
            $"Surviving to the end gives you the full amount of points from completing a chart. Losing will reduce this, depending on how far though the chart you were.\n" +
            $"Each point of damage you do deal will add points, and each point of damage you take will remove some."),
        new("Ranks", RanksText(0), RanksText),
        new("Chart rules",
            "Charts may have different rulesets; such as only allowing spawning of discs, no structures etc.\n" +
            $"Some charts will also break stationary structures after a few seconds, and some silently break structures that go past you."),
        new("Settings",
            "In Basalt's mod settings:\n" +
            "<align=left>Flashing: full, reduced or off (only if using RumbleReShaded)</align>\n" +
            "<align=left>Shader Intensity: chart effects intensity (only if using RumbleReShaded)</align>\n" +
            "<align=left>Phase Banners: show phase names (defaults OFF)</align>\n" +
            "<align=left>Menu here: bring this menu to you. In the Gym it will stay in that spot until moved again</align>"),
        new("Adding charts",
            $"Put chart packages ({ChartPackage.Extension}) in UserData/Basalt/Charts. They should appear without restarting the game."),
    ];

    private const string ParagraphBreak = "\n<size=15%> </size>\n";

    private int _topic;

    public override MenuSlider? Slider => new($"info:{Id}", Topics.Select(t => t.Title.ToUpperInvariant()).ToList(), _topic, step => _topic = step);

    public static string Page(string title, string text) =>
        $"<size=60%><b>{title}</b></size>\n<size=40%>{text.Trim().Replace("\n", ParagraphBreak, StringComparison.Ordinal)}</size>";

    public override MenuCard Card
    {
        get
        {
            var topic = Topics[_topic];
            return new MenuCard(Page(topic.Title, topic.Text), null, topic.TextAt is { } textAt ? time => Page(topic.Title, textAt(time)) : null);
        }
    }

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
