using UnityEngine;

namespace Basalt.Menu;

// What the menu shows for one mode: refreshed a few times a second while it shows.
internal interface IMenuScreen
{
    MenuCard Card { get; }

    IReadOnlyList<MenuButton> Buttons { get; }

    MenuSlider? Slider => null;

    MenuPlacement Placement => MenuPlacement.InFront;

    bool FollowsPlayer => false;
}

// What the card shows: rich text, and a cover image file or none.
internal record MenuCard(string Text, string? CoverPath = null);

// A button under the card; the id is for keys and tests.
internal record MenuButton(string Id, string Label, Color Colour, Action Press);

// A stepped slider beside the card, its first step at the top; the id is for keys and tests.
internal record MenuSlider(string Id, IReadOnlyList<string> Labels, int Step, Action<int> Choose);

// Where the menu stands when it opens.
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
