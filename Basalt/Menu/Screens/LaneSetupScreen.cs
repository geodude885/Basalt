using Basalt.Editing;
using static Basalt.Menu.MenuColours;

namespace Basalt.Menu.Screens;

// Laying out a chart's lane by walking the preview: beside you, and following you as you go.
internal class LaneSetupScreen(ChartSelection charts, Studio studio) : IMenuScreen
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
            var arena = studio.LaneSetup.ArenaSpec;
            var lane = studio.LaneSetup.Lane;
            var actor = studio.LaneSetup.Actors.FirstOrDefault();
            var where = $"{arena.Map}, from {arena.X:0.##}, {arena.Z:0.##} facing {arena.Facing:0}";
            return new MenuCard(
                $"<size=45%>LANE SETUP</size>\n{studio.Entry?.Name}\n<size=50%>{where}\n" +
                $"{lane.Length:0.#} m long  |  {lane.Width:0.#} m wide{(actor == null ? "" : $"  |  actor at {actor.At.X:0.#}, {actor.At.Z:0.#}")}</size>\n" +
                "<size=38%>walk the preview: LANE HERE starts the lane where you stand, facing the way you face; ACTOR HERE puts the actor where you stand</size>");
        }
    }
}
