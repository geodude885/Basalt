using static Basalt.Menu.MenuColours;

namespace Basalt.Menu.Pages;

// A page opened from a button on another page
internal abstract class PanelPage(string id) : IMenuScreen
{
    public bool IsOpen { get; private set; }

    public void Open() => IsOpen = true;

    public void Close() => IsOpen = false;

    public abstract MenuCard Card { get; }

    public virtual MenuSlider? Slider => null;

    public IReadOnlyList<MenuButton> Buttons => [new MenuButton($"{id}-back", "BACK", Red, Close)];

    protected string Id => id;
}
