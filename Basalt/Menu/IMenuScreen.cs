using UnityEngine;

namespace Basalt.Menu;

internal interface IMenuScreen
{
    MenuCard Card { get; }
    IReadOnlyList<MenuButton> Buttons { get; }
    MenuSlider? Slider => null;
    MenuPlacement Placement => MenuPlacement.InFront;
    bool FollowsPlayer => false;
}

internal record MenuCard(string Text, string? CoverPath = null);
internal record MenuButton(string Id, string Label, Color Colour, Action Press);
internal record MenuSlider(string Id, IReadOnlyList<string> Labels, int Step, Action<int> Choose);

internal enum MenuPlacement
{
    InFront,
    Beside,
    Behind,
}

internal static class MenuColours
{
    public static readonly Color Orange = new(1f, 0.45f, 0.1f);
    public static readonly Color Green = new(0.2f, 0.75f, 0.3f);
    public static readonly Color Red = new(0.85f, 0.2f, 0.2f);
    public static readonly Color Blue = new(0.25f, 0.5f, 0.95f);
}
