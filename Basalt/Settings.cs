using MelonLoader;
using MelonLoader.Utils;
using UIFramework.UiExtensions;

namespace Basalt;

internal class Settings
{
    // Raised when a setting that ModEntry applies changes.
    public event Action? Changed;

    public MelonPreferences_Category Category { get; private set; } = null!;
    public MelonPreferences_Entry<bool> Editing { get; private set; } = null!;
    public MelonPreferences_Entry<bool> ShowPhaseBanners { get; private set; } = null!;
    public MelonPreferences_Entry<float> ChartVisuals { get; private set; } = null!;
    public MelonPreferences_Entry<string> Chart { get; private set; } = null!; // Hidden
    public MelonPreferences_Entry<LogLevel> LogLevel { get; private set; } = null!; // Hidden

    public void Create()
    {
        var folder = Path.Combine(MelonEnvironment.UserDataDirectory, "Basalt");
        Directory.CreateDirectory(folder); // A category's file path fails silently if its folder is missing.

        Category = MelonPreferences.CreateCategory("Basalt", "Basalt");
        Category.SetFilePath(Path.Combine(folder, "settings.cfg"));

        ShowPhaseBanners = Applied(Category.CreateEntry("ShowPhaseBanners", false, "Show Chart Phase Banners", "Show chart phase names and point multipliers (if any)"));

        ChartVisuals = Applied(Category.CreateEntry("ChartVisuals", 1f, "Shader Intensity", "With RumbleReShaded, how intense shader effects are applied",
            false, false, new SliderDescriptor() { Min = 0f, Max = 1f, DecimalPlaces = 2 }));

        Editing = Category.CreateEntry("Editing", false, "Chart Editing", "Show chart creation/editing tools");

        Chart = Category.CreateEntry("Chart", "", "Chart", null, is_hidden: true);

        LogLevel = Applied(Category.CreateEntry("LogLevel", Basalt.LogLevel.Info, "Log level", null, is_hidden: true));
    }

    private MelonPreferences_Entry<T> Applied<T>(MelonPreferences_Entry<T> entry)
    {
        entry.OnEntryValueChanged.Subscribe((_, _) => Changed?.Invoke());
        return entry;
    }
}
