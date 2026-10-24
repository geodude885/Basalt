using MelonLoader;
using MelonLoader.Utils;
using UIFramework.UiExtensions;

namespace Basalt.UI;

// The mod's MelonPreferences, in UserData/Basalt/settings.cfg, shown by UIFramework.
internal class Settings
{
    // Raised when a setting that ModEntry applies changes.
    public event Action? Changed;

    public MelonPreferences_Category Category { get; private set; } = null!;

    // Not shown: the picker chooses the chart, and this remembers it.
    public MelonPreferences_Entry<string> Chart { get; private set; } = null!;

    public MelonPreferences_Entry<bool> Editing { get; private set; } = null!;

    public MelonPreferences_Entry<bool> ShowPhaseBanners { get; private set; } = null!;

    public MelonPreferences_Entry<float> ChartVisuals { get; private set; } = null!;

    // Not shown: set in settings.cfg when debugging.
    public MelonPreferences_Entry<LogLevel> LogLevel { get; private set; } = null!;

    public void Create()
    {
        var folder = Path.Combine(MelonEnvironment.UserDataDirectory, "Basalt");
        Directory.CreateDirectory(folder); // A category's file path fails silently if its folder is missing.

        Category = MelonPreferences.CreateCategory("Basalt", "Basalt");
        Category.SetFilePath(Path.Combine(folder, "settings.cfg"));

        Chart = Category.CreateEntry("Chart", "", "Chart", "The chosen chart's file or folder name, in UserData/Basalt/Charts.", is_hidden: true);
        Editing = Category.CreateEntry("Editing", false, "Chart editing",
            "Show the chart-making tools: SETUP, RECORD, MIME and UNDO on the picker, and New chart and Open chart folder here. Off: just play.");
        ShowPhaseBanners = Applied(Category.CreateEntry("ShowPhaseBanners", false, "Show section names",
            "Show each new section's name and score multiplier on the board as a battle goes. Off: just the countdown, GO and the result."));
        ChartVisuals = Applied(Category.CreateEntry("ChartVisuals", 1f, "Chart visuals",
            "With RumbleReShaded: how strongly a chart's shader looks and battle effects apply. 0 turns them off; your own pack settings come back after each battle.",
            false, false, Slider(0f, 1f, 2)));
        LogLevel = Applied(Category.CreateEntry("LogLevel", Basalt.LogLevel.Info, "Log level",
            "How much the mod writes to the MelonLoader console and log: Debug is everything, Error only failures.", is_hidden: true));
    }

    private static SliderDescriptor Slider(float min, float max, int decimals) => new() { Min = min, Max = max, DecimalPlaces = decimals };

    private MelonPreferences_Entry<T> Applied<T>(MelonPreferences_Entry<T> entry)
    {
        entry.OnEntryValueChanged.Subscribe((_, _) => Changed?.Invoke());
        return entry;
    }
}
