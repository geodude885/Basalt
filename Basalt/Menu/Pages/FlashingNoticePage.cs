using static Basalt.Menu.MenuColours;

namespace Basalt.Menu.Pages;

internal class FlashingNoticePage(ChartSelection charts) : IMenuScreen
{
    public IReadOnlyList<MenuButton> Buttons =>
    [
        new MenuButton("continue", "CONTINUE", Green, () => charts.AnswerFlashingNotice(reduce: false)),
        new MenuButton("reduce", "REDUCE", Orange, () => charts.AnswerFlashingNotice(reduce: true)),
    ];

    public MenuCard Card => new(
        "<size=60%><b>Photosensitivity warning</b></size>\n<size=38%>" +
        "Charts can include flashing lights and strobes that may trigger seizures in people with photosensitive epilepsy. " +
        "If you or anyone in your family has had a seizure or epilepsy, consult a doctor before playing. " +
        "Stop playing at once if you feel dizzy, see altered vision, or have eye or muscle twitches. " +
        "You can reduce or turn off flashing in Basalt's settings (Flashing).</size>");
}
