using BossBattles.Services.Charts;
using MelonLoader;
using MelonLoader.Utils;
using UIFramework.UiExtensions;

namespace BossBattles.UI;

/// <summary>The mod's MelonPreferences, in <c>UserData/BossBattles/settings.cfg</c>, shown by UIFramework.</summary>
internal sealed class Settings
{
    /// <summary>Where battles are played: the chart's own arena, or one for every chart.</summary>
    public enum ArenaChoice
    {
        Chart,
        Here,
        Gym,
        Ring,
        Pit,
    }

    private DynamicDropdownDescriptor? _chartDropdown;

    /// <summary>Raised when any setting that <see cref="ModEntry"/> applies changes.</summary>
    public event Action? Changed;

    public MelonPreferences_Category Category { get; private set; } = null!;

    public MelonPreferences_Entry<string> Chart { get; private set; } = null!;

    public MelonPreferences_Entry<ArenaChoice> Arena { get; private set; } = null!;

    public MelonPreferences_Entry<float> MusicVolume { get; private set; } = null!;

    public MelonPreferences_Entry<bool> ShowPicker { get; private set; } = null!;

    public MelonPreferences_Entry<float> PickerDistance { get; private set; } = null!;

    public MelonPreferences_Entry<float> PickerHeight { get; private set; } = null!;

    public MelonPreferences_Entry<float> PickerSide { get; private set; } = null!;

    public MelonPreferences_Entry<bool> PickerFixedInGym { get; private set; } = null!;

    public MelonPreferences_Entry<bool> Editing { get; private set; } = null!;

    /// <summary>Null plays each chart in its own arena.</summary>
    public ArenaKind? ArenaOverride => Arena.Value == ArenaChoice.Chart ? null : Enum.Parse<ArenaKind>(Arena.Value.ToString());

    public MelonPreferences_Entry<float> Countdown { get; private set; } = null!;

    public MelonPreferences_Entry<bool> ShowBackstop { get; private set; } = null!;

    public MelonPreferences_Entry<float> ChartVisuals { get; private set; } = null!;

    public MelonPreferences_Entry<bool> ShowPhaseBanners { get; private set; } = null!;

    public MelonPreferences_Entry<LogLevel> LogLevel { get; private set; } = null!;

    public void Create(IEnumerable<string> charts)
    {
        var folder = Path.Combine(MelonEnvironment.UserDataDirectory, "BossBattles");
        Directory.CreateDirectory(folder); // A category's file path fails silently if its folder is missing.

        Category = MelonPreferences.CreateCategory("BossBattles", "Boss Battles");
        Category.SetFilePath(Path.Combine(folder, "settings.cfg"));

        var names = charts.ToList();
        _chartDropdown = new DynamicDropdownDescriptor(Items(names));

        Chart = Category.CreateEntry("Chart", names.FirstOrDefault() ?? "Warm-up", "Chart",
            "Which battle to play. Charts live in UserData/BossBattles/Charts.", false, false, _chartDropdown);
        Arena = Category.CreateEntry("ArenaOverride", ArenaChoice.Chart, "Arena",
            "Chart: where each chart says. Here: wherever you stand, facing the way you face. Gym / Ring / Pit: that map for every chart.");
        MusicVolume = Applied(Category.CreateEntry("MusicVolume", 0.8f, "Music volume", "Volume of chart songs (the game's music volume applies too).",
            false, false, Slider(0f, 1f, 2)));
        ShowPicker = Applied(Category.CreateEntry("ShowPicker", true, "Show chart picker",
            "Stand a chart picker in front of you between battles: push or click its buttons."));
        PickerDistance = Applied(Category.CreateEntry("PickerDistance", 0.75f, "Picker distance (m)",
            "How far in front of your eyes the chart picker appears.", false, false, Slider(0.3f, 2f, 2)));
        PickerHeight = Applied(Category.CreateEntry("PickerHeight", -0.35f, "Picker height (m)",
            "The picker's height relative to your eyes; negative is below.", false, false, Slider(-1f, 0.5f, 2)));
        PickerSide = Applied(Category.CreateEntry("PickerSide", 0f, "Picker sideways (m)",
            "How far to your right the picker appears; negative is left.", false, false, Slider(-1f, 1f, 2)));
        Editing = Category.CreateEntry("Editing", false, "Chart editing",
            "Show the chart-making tools: RECORD on the picker (record an actor's performance into the chart), and the new chart, export and where-am-I keys. Off: just play.");
        PickerFixedInGym = Applied(Category.CreateEntry("PickerFixedInGym", true, "Fixed picker spot in the Gym",
            "In the Gym, Picker here sets a spot the picker then always stands at - saved in UserData/BossBattles/places.json. Off: it always comes to you."));
        Countdown = Applied(Category.CreateEntry("Countdown", 3f, "Countdown (s)", "Seconds to count down before a battle starts.",
            false, false, Slider(0f, 10f, 0)));
        ShowBackstop = Applied(Category.CreateEntry("ShowBackstop", true, "Show backstop",
            "Draw the wall behind you. It stops you and stray shots either way."));
        ShowPhaseBanners = Applied(Category.CreateEntry("ShowPhaseBanners", true, "Show section names",
            "Show each new section's name and score multiplier on the board as a battle goes. Off: just the countdown, GO and the result."));
        ChartVisuals = Applied(Category.CreateEntry("ChartVisuals", 1f, "Chart visuals",
            "With RumbleReShaded: how strongly a chart's shader looks and battle effects apply. 0 turns them off; your own pack settings come back after each battle.",
            false, false, Slider(0f, 1f, 2)));
        LogLevel = Applied(Category.CreateEntry("LogLevel", BossBattles.LogLevel.Info, "Log level",
            "How much the mod writes to the MelonLoader console and log: Debug is everything, Error only failures."));
    }

    public void UpdateChartChoices(IEnumerable<string> charts) => _chartDropdown?.SetDropdownItems(Items(charts));

    private static List<DropdownItem> Items(IEnumerable<string> names) => names.Select(n => new DropdownItem(n)).ToList();

    private static SliderDescriptor Slider(float min, float max, int decimals) => new() { Min = min, Max = max, DecimalPlaces = decimals };

    private MelonPreferences_Entry<T> Applied<T>(MelonPreferences_Entry<T> entry)
    {
        entry.OnEntryValueChanged.Subscribe((_, _) => Changed?.Invoke());
        return entry;
    }
}
