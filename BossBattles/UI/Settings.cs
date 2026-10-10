using BossBattles.Services.Charts;
using MelonLoader;
using MelonLoader.Preferences;
using MelonLoader.Utils;

namespace BossBattles.UI;

/// <summary>
/// The mod's settings, as MelonPreferences in <c>UserData/BossBattles/settings.cfg</c>. UIFramework,
/// when installed, shows them in its window with a chart dropdown and buttons (see
/// <see cref="UiBridge"/>); without it they are set in the file and driven from the keys.
/// </summary>
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

    private ValueValidator? _chartDropdown;

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

    /// <summary>The arena override for <see cref="Battle.BattleController.Start"/>: null plays each chart in its own.</summary>
    public ArenaKind? ArenaOverride => Arena.Value == ArenaChoice.Chart ? null : Enum.Parse<ArenaKind>(Arena.Value.ToString());

    public MelonPreferences_Entry<float> Countdown { get; private set; } = null!;

    public MelonPreferences_Entry<float> ActorKnockbackTaken { get; private set; } = null!;

    public MelonPreferences_Entry<bool> ShowBackstop { get; private set; } = null!;

    public MelonPreferences_Entry<float> ChartVisuals { get; private set; } = null!;

    public MelonPreferences_Entry<bool> ShowPhaseBanners { get; private set; } = null!;

    public void Create(IEnumerable<string> charts)
    {
        var folder = Path.Combine(MelonEnvironment.UserDataDirectory, "BossBattles");
        Directory.CreateDirectory(folder); // A category's file path fails silently if its folder is missing.

        Category = MelonPreferences.CreateCategory("BossBattles", "Boss Battles");
        Category.SetFilePath(Path.Combine(folder, "settings.cfg"));

        var names = charts.ToList();
        _chartDropdown = UiBridge.Dropdown(names);

        Chart = Category.CreateEntry("Chart", names.FirstOrDefault() ?? "Warm-up", "Chart",
            "Which battle to play. Charts live in UserData/BossBattles/Charts.", false, false, _chartDropdown);
        // A new id: the old "Arena" entry defaulted to Here, which would now override every chart.
        Arena = Category.CreateEntry("ArenaOverride", ArenaChoice.Chart, "Arena",
            "Chart: where each chart says. Here: wherever you stand, facing the way you face. Gym / Ring / Pit: that map for every chart.");
        MusicVolume = Category.CreateEntry("MusicVolume", 0.8f, "Music volume", "Volume of chart songs (the game's music volume applies too).",
            false, false, UiBridge.Slider(0f, 1f, 2));
        ShowPicker = Category.CreateEntry("ShowPicker", true, "Show chart picker",
            "Stand a chart picker in front of you between battles: push or click its buttons.");
        PickerDistance = Category.CreateEntry("PickerDistance", 0.75f, "Picker distance (m)",
            "How far in front of your eyes the chart picker appears.", false, false, UiBridge.Slider(0.3f, 2f, 2));
        PickerHeight = Category.CreateEntry("PickerHeight", -0.35f, "Picker height (m)",
            "The picker's height relative to your eyes; negative is below.", false, false, UiBridge.Slider(-1f, 0.5f, 2));
        PickerSide = Category.CreateEntry("PickerSide", 0f, "Picker sideways (m)",
            "How far to your right the picker appears; negative is left.", false, false, UiBridge.Slider(-1f, 1f, 2));
        Editing = Category.CreateEntry("Editing", false, "Chart editing",
            "Show the chart-making tools: RECORD on the picker (record an actor's performance into the chart), and the new chart, export and where-am-I keys. Off: just play.");
        PickerFixedInGym = Category.CreateEntry("PickerFixedInGym", true, "Fixed picker spot in the Gym",
            "In the Gym, Ctrl+M (or Picker here) sets a spot the picker then always stands at - saved in UserData/BossBattles/places.json. Off: it always comes to you.");
        Countdown = Category.CreateEntry("Countdown", 3f, "Countdown (s)", "Seconds to count down before a battle starts.",
            false, false, UiBridge.Slider(0f, 10f, 0));
        ActorKnockbackTaken = Category.CreateEntry("ActorKnockbackTaken", 0f, "Actor knockback taken",
            "How much of the game's knockback actors take: 0 they stand their ground, 1 like a player. They are planted either way.",
            false, false, UiBridge.Slider(0f, 1f, 2));
        ShowBackstop = Category.CreateEntry("ShowBackstop", true, "Show backstop",
            "Draw the wall behind you. It stops you and stray shots either way.");
        ShowPhaseBanners = Category.CreateEntry("ShowPhaseBanners", true, "Show section names",
            "Show each new section's name and score multiplier on the board as a battle goes. Off: just the countdown, GO and the result.");
        ChartVisuals = Category.CreateEntry("ChartVisuals", 1f, "Chart visuals",
            "With RumbleReShaded: how strongly a chart's shader looks and battle effects apply. 0 turns them off; your own pack settings come back after each battle.",
            false, false, UiBridge.Slider(0f, 1f, 2));
    }

    public void UpdateChartChoices(IEnumerable<string> charts) => UiBridge.SetDropdownItems(_chartDropdown, charts);
}
