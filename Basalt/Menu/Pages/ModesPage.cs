using Basalt.Services.Modes;

namespace Basalt.Menu.Pages;

internal class ModesPage(ChartSelection charts) : PanelPage("modes")
{
    public override MenuSlider? Slider => new(
        "modes",
        GameModes.All.Select(m => m.ToString().ToUpperInvariant()).ToList(),
        Index(charts.GameMode),
        step => charts.GameMode = GameModes.All[step]);

    public override MenuCard Card => new(InfoPage.Page($"Mode: {charts.GameMode}", charts.GameMode.Describe()));

    private static int Index(GameMode mode) => Math.Max(0, GameModes.All.ToList().IndexOf(mode));
}
