using Basalt.Editing;
using static Basalt.Menu.MenuColours;

namespace Basalt.Menu.Pages;

internal class ReviewRecordingPage(ChartSelection charts, Studio studio) : IMenuScreen
{
    public IReadOnlyList<MenuButton> Buttons =>
    [
        new MenuButton("keep", "KEEP", Green, () => charts.Saved(studio.KeepClip)),
        new MenuButton("again", "AGAIN", Orange, studio.RestartRecording),
        new MenuButton("replay", "REPLAY", Blue, studio.ReviewRecording),
        new MenuButton("discard", "DISCARD", Red, studio.DiscardClip),
    ];

    public MenuCard Card => studio.NewClip is not { } clip
        ? new MenuCard("CLIP")
        : new MenuCard(
            $"<size=45%>CLIP</size>\n{clip.Name}\n<size=55%>{clip.Duration - clip.Start:0.0}s\n" +
            $"{clip.Moves.Count} move{(clip.Moves.Count == 1 ? "" : "s")}</size>");
}
