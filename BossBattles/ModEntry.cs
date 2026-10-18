using System.Globalization;
using BossBattles.Actors;
using BossBattles.Arenas;
using BossBattles.Battle;
using BossBattles.Editing;
using BossBattles.Patches;
using BossBattles.Presentation;
using BossBattles.Services;
using BossBattles.Services.Charts;
using BossBattles.Services.Scoring;
using BossBattles.UI;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

[assembly: MelonInfo(typeof(BossBattles.ModEntry), "BossBattles", "1.0.0", "ACutiePi")]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]
[assembly: MelonAdditionalDependencies("UIFramework")]
[assembly: MelonOptionalDependencies("RumbleReShaded")]

// MelonLoader would otherwise PatchAll this assembly before OnInitializeMelon.
[assembly: HarmonyDontPatchAll]

namespace BossBattles;

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
        var folder = Path.Combine(MelonEnvironment.UserDataDirectory, "BossBattles");

        var library = new ChartLibrary(Path.Combine(folder, "Charts"));
        var scores = new JsonBook<BestScore>(Path.Combine(folder, "scores.json"));
        scores.Load();
        if (scores.LoadError != null) Logger.Info($"scores.json could not be read ({scores.LoadError}) - starting a fresh book");
        var pickerSpots = new JsonBook<SavedSpot>(Path.Combine(folder, "places.json"));
        pickerSpots.Load();

        _assets = new GameAssets();
        _actors = new ActorRegistry();
        _battle = new BattleController(_actors, scores, _assets);
        _studio = new Studio(_battle, _assets);
        _charts = new ChartSelection(library, _settings, _battle, _studio, Path.Combine(folder, "Exports"));
        _picker = new ChartPicker(new PickerPanel(_charts, library, scores, _settings, _battle, _studio), pickerSpots, _assets);
        Mod.Share(_actors, _battle, _studio, _charts, _picker);

        // The charts are read before the settings are made: the chart dropdown lists them.
        _charts.ReloadCharts();
        _settings.Create(library.Playable.Select(e => e.Name));
        ApplySettings();
        _settings.Changed += ApplySettings;

        Logger.Debug($"[Init] actor patches: {ActorPatches.Apply(HarmonyInstance, _actors, _battle)} methods");
        Logger.Debug($"[Init] arena patches: {ArenaPatches.Apply(HarmonyInstance, _battle)} methods");

        foreach ((var text, var name, var description, var handler) in new (string, string, string, Action)[]
        {
            ("Start", "Start battle", "Play the chosen chart in the chosen arena.", _charts.StartBattle),
            ("Stop", "Stop battle", "End the battle and remove its actors.", () => _battle.Stop("stopped from the menu")),
            ("Reload", "Reload charts", "Read UserData/BossBattles/Charts again.", _charts.ReloadCharts),
            ("Picker", "Chart picker here", "Bring the chart picker in front of you - in the Gym, this sets its fixed spot.", _picker.Summon),
            ("New", "New chart", "Chart editing: make a new chart folder from a template, and pick it.", () => IfEditing(() => _charts.NewChart())),
            ("Folder", "Open chart folder", "Chart editing: open the chosen chart's folder, to edit it in the Boss Battles Chart Editor.", () => IfEditing(_charts.OpenChartFolder)),
            ("Record", "Record actor", "Chart editing: record the chosen chart's actor - you stand in its place and perform.", () => IfEditing(() => _charts.RecordChosen())),
            ("Export", "Export chart", "Chart editing: pack the chosen chart, its song and clips into UserData/BossBattles/Exports/<name>.bbchart.", () => IfEditing(_charts.ExportChosen)),
            ("Where", "Where am I", "Chart editing: print your spot as a chart's arena line, in the Logger.", () => IfEditing(WhereAmI)),
            ("Gym", "Back to the Gym", "Leave the Ring or Pit.", _battle.ReturnToGym),
        })
        {
            UIFramework.UI.CreateButtonEntry(_settings.Category, text, name, description, handler);
        }

        UIFramework.UI.RegisterMelon(this, _settings.Category);
        Logger.Info($"BossBattles loaded: {library.Playable.Count()} chart(s)");
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
        _battle.CountdownSeconds = Math.Max(0f, _settings.Countdown.Value);
        _battle.ShowBackstop = _settings.ShowBackstop.Value;
        _battle.ShowPhaseBanners = _settings.ShowPhaseBanners.Value;
        _battle.MusicVolume = Math.Clamp(_settings.MusicVolume.Value, 0f, 1f);
        _battle.Signals.Strength = _settings.ChartVisuals.Value;
        _picker.Open = _settings.ShowPicker.Value;
        _picker.Distance = Math.Clamp(_settings.PickerDistance.Value, 0.3f, 2f);
        _picker.Height = Math.Clamp(_settings.PickerHeight.Value, -1f, 0.5f);
        _picker.Side = Math.Clamp(_settings.PickerSide.Value, -1f, 1f);
        _picker.FixedInGym = _settings.PickerFixedInGym.Value;
    }

    private void IfEditing(Action action)
    {
        if (_settings.Editing.Value) action();
        else Logger.Info("turn on Chart editing in the BossBattles settings to use the chart-making tools");
    }

    // Prints your spot as a chart's "arena" line.
    private static void WhereAmI()
    {
        if (BattlePlayer.Controller is not { } you) return;

        var map = Maps.Current;
        var rig = ActorRig.From(you);
        var at = rig.Feet - (map.GetArenaOrigin() ?? Vector3.zero);
        static string Number(float value)
        {
            return value.ToString("0.0#", CultureInfo.InvariantCulture);
        }

        Logger.Info(map == ArenaKind.Here
            ? $"[Where] {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} is not a chart map (gym, ring, pit)"
            : $"[Where] \"arena\": {{ \"map\": \"{map.ToString().ToLowerInvariant()}\", \"at\": {{ \"x\": {Number(at.x)}, \"z\": {Number(at.z)} }}, \"facing\": {rig.Facing.Bearing().ToString("0", CultureInfo.InvariantCulture)} }}");
    }
}
