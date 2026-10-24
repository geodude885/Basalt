using Basalt.Actors;
using Basalt.Arenas;
using Basalt.Battle;
using Basalt.Editing;
using Basalt.Patches;
using Basalt.Presentation;
using Basalt.Services;
using Basalt.Services.Charts;
using Basalt.Services.Scoring;
using Basalt.UI;
using MelonLoader;
using MelonLoader.Utils;

[assembly: MelonInfo(typeof(Basalt.ModEntry), "Basalt", "1.0.0", "ACutiePi")]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]
[assembly: MelonAdditionalDependencies("UIFramework")]
[assembly: MelonOptionalDependencies("RumbleReShaded")]

// MelonLoader would otherwise PatchAll this assembly before OnInitializeMelon.
[assembly: HarmonyDontPatchAll]

namespace Basalt;

// Builds the mod's services, hands each what it needs, and passes MelonLoader's calls on to them.
public class ModEntry : MelonMod
{
    private readonly Settings _settings = new();
    private ActorRegistry _actors = null!;
    private BattleController _battle = null!;
    private Studio _studio = null!;
    private ChartSelection _charts = null!;
    private ChartPicker _picker = null!;
    private GameAssets _assets = null!;

    public override void OnInitializeMelon()
    {
        Logger.Melon = LoggerInstance;
        var folder = Path.Combine(MelonEnvironment.UserDataDirectory, "Basalt");

        var library = new ChartLibrary(Path.Combine(folder, "Charts"));
        var scores = new JsonBook<BestScore>(Path.Combine(folder, "scores.json"));
        scores.Load();
        if (scores.LoadError != null) Logger.Info($"scores.json could not be read ({scores.LoadError}) - starting a fresh book");
        var pickerSpots = new JsonBook<SavedSpot>(Path.Combine(folder, "places.json"));
        pickerSpots.Load();

        _assets = new GameAssets();
        _actors = new ActorRegistry();
        _battle = new BattleController(_actors, scores, _assets, _settings);
        _studio = new Studio(_battle, _assets);
        _charts = new ChartSelection(library, _settings, _battle, _studio);
        _picker = new ChartPicker(new ChartPickerPanel(_charts, library, scores, _settings, _battle, _studio), pickerSpots, _assets);
        Mod.Share(_actors, _battle, _studio, _charts, _picker);

        _settings.Create();
        _charts.ReloadCharts();
        ApplySettings();
        _settings.Changed += ApplySettings;

        Logger.Debug($"[Init] actor patches: {ActorPatches.Apply(HarmonyInstance, _actors, _battle)} methods");
        Logger.Debug($"[Init] arena patches: {ArenaPatches.Apply(HarmonyInstance, _battle)} methods");

        foreach ((var text, var name, var description, var handler) in new (string, string, string, Action)[]
        {
            ("Stop", "Stop battle", "End the battle and remove its actors.", () => _battle.Stop("stopped from the menu")),
            ("Picker", "Chart picker here", "Bring the chart picker in front of you - in the Gym, this sets its fixed spot.", _picker.Summon),
            ("New", "New chart", "Chart editing: make a new chart folder from a template, and pick it.", () => IfEditing(() => _charts.NewChart())),
            ("Folder", "Open chart folder", "Chart editing: open the chosen chart's folder, to edit it in the Basalt Editor.", () => IfEditing(_charts.OpenChartFolder)),
        })
        {
            UIFramework.UI.CreateButtonEntry(_settings.Category, text, name, description, handler);
        }

        UIFramework.UI.RegisterMelon(this, _settings.Category);
        Logger.Info($"Basalt loaded: {library.Playable.Count()} chart(s)");
    }

    // A throw escaping a MelonLoader callback repeats every frame.
    public override void OnUpdate()
    {
        try
        {
            _charts.ReloadChartsWhenChanged();
            _actors.Update();
            _battle.Update();
            _studio.Update();
            _picker.Update();
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
            _actors.LateUpdate();
            ActorSoundScope.Reset();
        }
        catch (Exception e)
        {
            Logger.Error($"[LateUpdateFailed] {e}");
        }
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        _actors.ForgetSceneObjects();
        _battle.OnSceneLoaded(sceneName);
        _studio.OnSceneLoaded();
        _picker.Forget();
        if (sceneName == Maps.GymScene) _assets.CaptureRockTemplate();
    }

    private void ApplySettings()
    {
        Logger.Level = _settings.LogLevel.Value;
        _battle.Signals.Strength = _settings.ChartVisuals.Value;
    }

    private void IfEditing(Action action)
    {
        if (_settings.Editing.Value) action();
        else Logger.Info("turn on Chart editing in the Basalt settings to use the chart-making tools");
    }
}
