using Basalt.Editing;
using static Basalt.Menu.MenuColours;

namespace Basalt.Menu.Pages;

internal class RecordingPage(Studio studio) : IMenuScreen
{
    public IReadOnlyList<MenuButton> Buttons => [new MenuButton("stop", "STOP", Red, studio.StopRecording)];

    public MenuPlacement Placement => studio.Mime ? MenuPlacement.Behind : MenuPlacement.Beside;

    public MenuCard Card => studio.Mime
        ? new MenuCard($"<color=#ffa040>MIMING</color>\n{studio.ActorName}\n<size=60%>{studio.Recorder.Length:0.0}s</size>\n")
        : new MenuCard($"<color=#ff5050>RECORDING</color>\n{studio.ActorName}\n<size=60%>{studio.Recorder.Length:0.0}s</size>\n");
}
