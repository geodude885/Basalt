using BossBattles.Editing;
using BossBattles.Patches;
using BossBattles.Services.Charts;
using BossBattles.Services.Placement;
using BossBattles.Services.Scoring;
using BossBattles.UI;
using MelonLoader;
using MelonLoader.Utils;

[assembly: MelonInfo(typeof(BossBattles.ModEntry), "BossBattles", "1.0.0", "ACutiePi")]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]
[assembly: MelonAdditionalDependencies("UIFramework")]
[assembly: MelonOptionalDependencies("RumbleReShaded")]

// MelonLoader would otherwise PatchAll this assembly before OnInitializeMelon.
[assembly: HarmonyDontPatchAll]

namespace BossBattles;

/// <summary>The mod's entry point: settings and wiring.</summary>
public partial class ModEntry : MelonMod
{
    private readonly Settings _settings = new();
    private ChartLibrary _library = null!;
    private ScoreBook _scores = null!;
    private string _folder = "";

    internal static ModEntry? Instance { get; private set; }

    internal ChartPicker Picker { get; private set; } = null!;

    public override void OnInitializeMelon()
    {
        Instance = this;
        _folder = Path.Combine(MelonEnvironment.UserDataDirectory, "BossBattles");
        Logger.Melon = LoggerInstance;

        _library = new ChartLibrary(Path.Combine(_folder, "Charts"));
        _scores = new ScoreBook(Path.Combine(_folder, "scores.json"));
        _scores.Load();
        if (_scores.LoadError != null) Logger.Info($"scores.json could not be read ({_scores.LoadError}) - starting a fresh book");
        Mod.Battle.Scores = _scores;
        var places = new SpotBook(Path.Combine(_folder, "places.json"));
        places.Load();
        Picker = new ChartPicker(DescribePanel, PanelButtons, PanelWanted, places);
        ReloadCharts();

        _settings.Create(_library.Playable.Select(e => e.Name));
        ApplySettings();
        _settings.Changed += ApplySettings;

        Logger.Debug($"[Init] actor patches: {ActorPatches.Apply(HarmonyInstance)} methods");
        Logger.Debug($"[Init] arena patches: {ArenaPatches.Apply(HarmonyInstance)} methods");

        foreach ((var text, var name, var description, var handler) in new (string, string, string, Action)[]
        {
            ("Start", "Start battle", "Play the chosen chart in the chosen arena.", StartBattle),
            ("Stop", "Stop battle", "End the battle and remove its actors.", () => Mod.Battle.Stop("stopped from the menu")),
            ("Reload", "Reload charts", "Read UserData/BossBattles/Charts again.", ReloadCharts),
            ("Picker", "Chart picker here", "Bring the chart picker in front of you - in the Gym, this sets its fixed spot.", Picker.Summon),
            ("New", "New chart", "Chart editing: make a new chart folder from a template, and pick it.", () => IfEditing(() => NewChart())),
            ("Folder", "Open chart folder", "Chart editing: open the chosen chart's folder, to edit it in the Boss Battles Chart Editor.", () => IfEditing(OpenChartFolder)),
            ("Record", "Record actor", "Chart editing: record the chosen chart's actor - you stand in its place and perform.", () => IfEditing(() => RecordChosen())),
            ("Export", "Export chart", "Chart editing: pack the chosen chart, its song and clips into UserData/BossBattles/Exports/<name>.bbchart.", () => IfEditing(ExportChosen)),
            ("Where", "Where am I", "Chart editing: print your spot as a chart's arena line, in the Logger.", () => IfEditing(WhereAmI)),
            ("Gym", "Back to the Gym", "Leave the Ring or Pit.", Mod.Battle.ReturnToGym),
        })
        {
            UIFramework.UI.CreateButtonEntry(_settings.Category, text, name, description, handler);
        }

        UIFramework.UI.RegisterMelon(this, _settings.Category);
        Logger.Info($"BossBattles loaded: {_library.Playable.Count()} chart(s)");
    }

    // A throw escaping a MelonLoader callback repeats every frame.
    public override void OnUpdate()
    {
        try
        {
            ReloadChartsWhenChanged();
            Mod.Actors.Update();
            Mod.Battle.Update();
            Mod.Studio.Update();
            Picker.Placement = Mod.Studio.State is StudioState.Recording or StudioState.Setup ? PickerPlacement.Beside : PickerPlacement.InFront;
            Picker.Follow = Mod.Studio.State == StudioState.Setup;
            Picker.Update();
        }
        catch (Exception e)
        {
            Logger.Error($"[UpdateFailed] {e}");
        }
    }

    public override void OnLateUpdate()
    {
        try
        {
            Mod.Actors.LateUpdate();
            ActorSoundScope.Reset();
        }
        catch (Exception e)
        {
            Logger.Error($"[LateUpdateFailed] {e}");
        }
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        Mod.Actors.ForgetSceneObjects();
        Mod.Battle.OnSceneLoaded(sceneName);
        Mod.Studio.OnSceneLoaded();
        Picker.Forget();
    }

    private void ApplySettings()
    {
        Logger.Level = _settings.LogLevel.Value;
        Mod.Battle.CountdownSeconds = Math.Max(0f, _settings.Countdown.Value);
        Mod.Battle.ShowBackstop = _settings.ShowBackstop.Value;
        Mod.Battle.ShowPhaseBanners = _settings.ShowPhaseBanners.Value;
        Mod.Battle.MusicVolume = Math.Clamp(_settings.MusicVolume.Value, 0f, 1f);
        Mod.Battle.Signals.Strength = _settings.ChartVisuals.Value;
        Picker.Open = _settings.ShowPicker.Value;
        Picker.Distance = Math.Clamp(_settings.PickerDistance.Value, 0.3f, 2f);
        Picker.Height = Math.Clamp(_settings.PickerHeight.Value, -1f, 0.5f);
        Picker.Side = Math.Clamp(_settings.PickerSide.Value, -1f, 1f);
        Picker.FixedInGym = _settings.PickerFixedInGym.Value;
    }
}
