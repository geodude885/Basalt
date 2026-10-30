using Basalt.Editing;
using static Basalt.Menu.MenuColours;

namespace Basalt.Menu.Pages;

internal class LaneSetupPage(ChartSelection charts, Studio studio) : IMenuScreen
{
    public IReadOnlyList<MenuButton> Buttons =>
    [
        new MenuButton("longer", "LONGER", Orange, () => studio.LaneSetup.ResizeLane(1f, 0f)),
        new MenuButton("shorter", "SHORTER", Orange, () => studio.LaneSetup.ResizeLane(-1f, 0f)),
        new MenuButton("wider", "WIDER", Orange, () => studio.LaneSetup.ResizeLane(0f, 0.5f)),
        new MenuButton("narrower", "NARROWER", Orange, () => studio.LaneSetup.ResizeLane(0f, -0.5f)),
        new MenuButton("map", "MAP", Blue, studio.LaneSetup.SwitchToNextMap),
        new MenuButton("lanehere", "LANE HERE", Blue, studio.LaneSetup.StartLaneWherePlayerStands),
        new MenuButton("actorhere", "ACTOR HERE", Blue, studio.LaneSetup.MoveFirstActorWherePlayerStands),
        new MenuButton("save", "SAVE", Green, () => charts.Saved(studio.SaveChartOptions)),
        new MenuButton("cancel", "CANCEL", Red, studio.ExitChartOptions),
    ];

    public MenuPlacement Placement => MenuPlacement.Beside;

    public bool FollowsPlayer => true;

    public MenuCard Card
    {
        get
        {
            var arena = studio.LaneSetup.ArenaSpecification;
            var lane = studio.LaneSetup.Lane;
            var where = $"{arena.Map}, from {arena.X:0.##}, {arena.Z:0.##} facing {arena.Facing:0}";
            return new MenuCard(
                $"<size=45%>LANE SETUP</size>\n{studio.Entry?.Name}\n<size=50%>{where}\n" +
                $"{lane.Length:0.#} m long  |  {lane.Width:0.#} m wide</size>\n");
        }
    }
}
