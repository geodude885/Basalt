using System.Runtime.CompilerServices;
using BossBattles.Core;
using MelonLoader;
using MelonLoader.Preferences;
using UIFramework.UiExtensions;

namespace BossBattles.UI;

/// <summary>
/// Every touch of UIFramework, which is optional. A missing assembly throws when the *calling*
/// method is compiled, so each use is its own <c>NoInlining</c> method whose signature names only
/// MelonLoader types, called inside a try/catch: without UIFramework the settings still exist and
/// still save, just with no in-game window.
/// </summary>
internal static class UiBridge
{
    public static bool Available { get; private set; }

    /// <summary>A dropdown validator for UIFramework, or null without it. Keep the result to update its items later.</summary>
    public static ValueValidator? Dropdown(IEnumerable<string> items)
    {
        try
        {
            return CreateDropdown(items.ToList());
        }
        catch (Exception e) when (IsMissingAssembly(e))
        {
            return null;
        }
    }

    public static void SetDropdownItems(ValueValidator? dropdown, IEnumerable<string> items)
    {
        if (dropdown == null) return;

        try
        {
            ReplaceDropdownItems(dropdown, items.ToList());
        }
        catch (Exception e) when (IsMissingAssembly(e))
        {
            // Without UIFramework there is no dropdown to update.
        }
    }

    public static ValueValidator? Slider(float min, float max, int decimals)
    {
        try
        {
            return CreateSlider(min, max, decimals);
        }
        catch (Exception e) when (IsMissingAssembly(e))
        {
            return null;
        }
    }

    /// <summary>Registers the settings window and its buttons. Returns false without UIFramework.</summary>
    public static bool Register(MelonBase mod, MelonPreferences_Category category, IReadOnlyList<(string Text, string Name, string Description, Action Handler)> buttons)
    {
        try
        {
            RegisterWithFramework(mod, category, buttons);
            Available = true;
        }
        catch (Exception e)
        {
            Mod.Note("UI.NotRegistered", IsMissingAssembly(e) ? "UIFramework is not installed - settings work through the keys and MelonPreferences.cfg" : e.ToString());
            Available = false;
        }

        return Available;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static DynamicDropdownDescriptor CreateDropdown(List<string> items) =>
        new(items.Select(i => new DropdownItem(i)).ToList());

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReplaceDropdownItems(ValueValidator dropdown, List<string> items)
    {
        if (dropdown is DynamicDropdownDescriptor descriptor)
            descriptor.SetDropdownItems(items.Select(i => new DropdownItem(i)).ToList());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static SliderDescriptor CreateSlider(float min, float max, int decimals) =>
        new() { Min = min, Max = max, DecimalPlaces = decimals };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RegisterWithFramework(MelonBase mod, MelonPreferences_Category category, IReadOnlyList<(string Text, string Name, string Description, Action Handler)> buttons)
    {
        foreach ((var text, var name, var description, Action handler) in buttons)
            UIFramework.UI.CreateButtonEntry(category, text, name, description, handler);

        UIFramework.UI.RegisterMelon(mod, category);
    }

    private static bool IsMissingAssembly(Exception e) =>
        e is FileNotFoundException or FileLoadException or TypeLoadException
        || (e.InnerException != null && IsMissingAssembly(e.InnerException));
}
