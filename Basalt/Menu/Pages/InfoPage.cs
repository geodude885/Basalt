using Basalt.Services.Help;
using static Basalt.Menu.MenuColours;

namespace Basalt.Menu.Pages;

internal class InfoPage(string id, IReadOnlyList<HelpTopic> topics) : IMenuScreen
{
    public void Open() => IsOpen = true;
    public void Close() => IsOpen = false;

    private int _topic;
    public bool IsOpen { get; private set; }

    public IReadOnlyList<MenuButton> Buttons => [new MenuButton($"{id}-back", "BACK", Green, Close)];

    public MenuSlider? Slider => topics.Count < 2
        ? null
        : new MenuSlider($"info:{id}", topics.Select(t => t.Title.ToUpperInvariant()).ToList(), _topic, step => _topic = step);

    private const string ParagraphBreak = "\n<size=15%> </size>\n";

    public MenuCard Card
    {
        get
        {
            if (topics.Count == 0) return new MenuCard("Nothing here yet");

            var topic = topics[_topic];
            return new MenuCard(Page(topic.Text), null, topic.TextAt is { } textAt ? time => Page(textAt(time)) : null);

            string Page(string text)
            {
                return $"<size=60%><b>{topic.Title}</b></size>\n<align=left><size=40%>{text.Trim().Replace("\n", ParagraphBreak, StringComparison.Ordinal)}</size></align>";
            }
        }
    }
}
