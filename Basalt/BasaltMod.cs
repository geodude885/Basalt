using Basalt.Actors;
using Basalt.Arenas;
using Basalt.Battle;
using Basalt.Editing;
using Basalt.Menu;
using Basalt.Menu.Pages;
using Basalt.Patches;
using Basalt.BattlePresentation;
using Basalt.Services;
using Basalt.Services.Charts;
using Basalt.Services.Scoring;
using MelonLoader;
using MelonLoader.Utils;

[assembly: MelonInfo(typeof(Basalt.BasaltMod), "Basalt", "1.0.0", "ACutiePi")]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]
[assembly: MelonAdditionalDependencies("UIFramework")]
[assembly: MelonOptionalDependencies("RumbleReShaded")]

[assembly: HarmonyDontPatchAll]

namespace Basalt;

public class BasaltMod : MelonMod
{
    internal static ActorsRegistry Actors { get; private set; } = null!;
    internal static BattleController Battle { get; private set; } = null!;
    internal static Studio Studio { get; private set; } = null!;
    internal static ChartSelection Charts { get; private set; } = null!;
    internal static BasaltMenu Menu { get; private set; } = null!;

    private readonly Settings _settings = new();
    private GameAssets _assets = null!;

    public override void OnInitializeMelon()
    {
        Logger.Melon = LoggerInstance;
        var folder = Path.Combine(MelonEnvironment.UserDataDirectory, "Basalt");

        var library = new ChartLibrary(Path.Combine(folder, "Charts"));

        var scores = new JsonFileRepository<BestScore>(Path.Combine(folder, "scores.json"));
        scores.Load();
        if (scores.LoadError != null) Logger.Info($"scores.json could not be read, starting a fresh store");

        var menuSpots = new JsonFileRepository<SavedSpot>(Path.Combine(folder, "places.json"));
        menuSpots.Load();

        _assets = new GameAssets();

        Actors = new ActorsRegistry();
        Battle = new BattleController(Actors, scores, _assets, _settings);
        Studio = new Studio(Battle, _assets);
        Charts = new ChartSelection(library, _settings, Battle, Studio);
        Menu = new BasaltMenu(new PageSwitcher(Charts, library, scores, _settings, Battle, Studio), menuSpots, _assets);

        _settings.Create();
        Charts.ReloadCharts();
        ApplySettings();
        _settings.Changed += ApplySettings;

        Logger.Debug($"[Init] actor patches: {ActorPatches.Apply(HarmonyInstance, Actors, Battle)} methods");
        Logger.Debug($"[Init] arena patches: {ArenaPatches.Apply(HarmonyInstance, Battle)} methods");

        foreach ((var text, var name, var description, var handler) in new (string, string, string, Action)[]
        {
            ("Stop", "Stop battle", "End the battle and remove its actors.", () => Battle.Stop("stopped from the menu")),
            ("Menu", "Menu here", "Bring the Basalt menu in front of you - in the Gym, this sets its fixed spot.", Menu.Summon),
            ("New", "New chart", "Chart editing: make a new chart folder from a template, and pick it.", () => IfEditing(() => Charts.NewChart())),
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
            Charts.ReloadChartsWhenChanged();
            Actors.Update();
            Battle.Update();
            Studio.Update();
            Menu.Update();
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
            Actors.LateUpdate();
            ActorSoundScope.Reset();
        }
        catch (Exception e)
        {
            Logger.Error($"[LateUpdateFailed] {e}");
        }
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        Actors.ForgetSceneObjects();
        Battle.OnSceneLoaded(sceneName);
        Studio.OnSceneLoaded();
        Menu.Forget();
        if (sceneName == Maps.GymScene && !_assets.CaptureMenuParts())
            Logger.Error("[Assets.MenuPartsMissing] the Gym's menu slab rock, tutorial button or menu slider assets were not found");
    }

    private void ApplySettings()
    {
        Logger.Level = _settings.LogLevel.Value;
        Battle.Visuals.ShaderIntensity = _settings.ChartVisuals.Value;
    }

    private void IfEditing(Action action)
    {
        if (_settings.Editing.Value) action();
        else Logger.Info("turn on Chart editing in the Basalt settings to use the chart-making tools");
    }
}
