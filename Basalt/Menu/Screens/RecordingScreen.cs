using Basalt.Editing;
using static Basalt.Menu.MenuColours;

namespace Basalt.Menu.Screens;

// While recording: beside you, or behind you while miming so it is clear of your arms.
internal class RecordingScreen(Studio studio) : IMenuScreen
{
    public IReadOnlyList<MenuButton> Buttons => [new MenuButton("stop", "STOP", Red, studio.StopRecording)];

    public MenuPlacement Placement => studio.Mime ? MenuPlacement.Behind : MenuPlacement.Beside;

    public MenuCard Card
    {
        get
        {
            (var seconds, var moves) = studio.Progress;
            return studio.Mime
                ? new MenuCard($"<color=#ffa040>MIMING</color>\n{studio.ActorName}\n<size=60%>{seconds:0.0}s</size>\n<size=45%>throw the chart's shots by hand - nothing spawns. STOP when done</size>")
                : new MenuCard($"<color=#ff5050>RECORDING</color>\n{studio.ActorName}\n<size=60%>{seconds:0.0}s  |  {moves} move{(moves == 1 ? "" : "s")}</size>\n<size=45%>STOP when done</size>");
        }
    }
}
