using Basalt.Battle;
using Basalt.DevTools.Flatscreen;
using Basalt.DevTools.Testing;
using Il2CppRUMBLE.Players.Subsystems;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

[assembly: MelonInfo(typeof(Basalt.DevTools.DevToolsEntry), "Basalt.DevTools", "1.0.0", "ACutiePi")]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]
[assembly: MelonAdditionalDependencies("Basalt")]
[assembly: HarmonyDontPatchAll]

namespace Basalt.DevTools;

/// <summary>Developer tools for Basalt: the self-tests, a keyboard-driven test actor, and flatscreen play.</summary>
public class DevToolsEntry : MelonMod
{
    private const string Keys =
        "Ctrl+W watch charts from above the lane, invincible | Ctrl+F desktop camera | Ctrl+T test actor | Ctrl+9 test actor mirror/bot | Ctrl+P plant | " +
        "Ctrl+K knockback. Click pushes game buttons. No headset: mouse looks, WASD walks, Q/E turn, Alt frees the cursor.";

    /// <summary>Orbits the battle's first actor, the test actor, or you.</summary>
    private readonly DesktopCamera _camera = new(
        () => Mod.Actors.Actors.FirstOrDefault(a => a.Ready)?.Body?.Rig.Head.position ?? Camera.main?.transform.position,
        message => Logger.Info($"[DesktopCamera] {message}"));

    private MelonPreferences_Entry<bool> _invincibleInCharts = null!;
    private ChartWatcher _watcher = null!;

    public override void OnInitializeMelon()
    {
        _watcher = new ChartWatcher(_camera);
        if (Mod.Menu is null)
        {
            LoggerInstance.Error("Basalt is not loaded - the dev tools are off");
            return;
        }

        FlatscreenControls.Log = message => Logger.Info($"[Flatscreen] {message}");
        Logger.Debug($"[Init] flatscreen controls: {FlatscreenControls.Apply(HarmonyInstance)} methods");
        SelfTest.Configure(_camera, _watcher);

        var settings = MelonPreferences.CreateCategory("BasaltDevTools", "Basalt DevTools");
        settings.SetFilePath(Path.Combine(MelonEnvironment.UserDataDirectory, "Basalt", "devtools.cfg"));
        _invincibleInCharts = settings.CreateEntry("InvincibleInCharts", false, "Invincible in charts",
            "While a chart plays (not while recording), your health is kept full: hits still count against the score.");
        UIFramework.UI.RegisterMelon(this, settings);
        Logger.Info($"Basalt.DevTools loaded. {Keys}");
    }

    public override void OnUpdate()
    {
        try
        {
            HandleKeys();
            KeepInvincibleInCharts();
            _watcher.Update();
            TestBench.Update();
            FlatscreenControls.Update(_camera.Camera);
            SelfTest.Update();
        }
        catch (Exception e)
        {
            Logger.Error($"[DevTools.UpdateFailed] {e}");
        }
    }

    public override void OnLateUpdate() => _camera.LateUpdate();

    private void KeepInvincibleInCharts()
    {
        if (!(_invincibleInCharts.Value || _watcher.Watching) || Mod.Battle.Run?.State != BattleState.Running || Mod.Battle.Run?.Request.Mode == BattleMode.Record) return;
        if (BattlePlayer.Player is not { Controller: not null } localPlayer) return;

        var health = localPlayer.Data.HealthPoints;
        if (health < PlayerHealth.MAX_PLAYER_HEALTH) localPlayer.Controller.PlayerHealth.SetHealth((short)PlayerHealth.MAX_PLAYER_HEALTH, health, false);
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        TestBench.Forget();
        _camera.Forget();
        SelfTest.OnSceneLoaded(sceneName);
    }

    private void HandleKeys()
    {
        if (!FlatscreenKeys.CtrlHeld) return;

        if (Input.GetKeyDown(KeyCode.W)) _watcher.Toggle();
        else if (Input.GetKeyDown(KeyCode.F)) _camera.Toggle();
        else if (Input.GetKeyDown(KeyCode.T)) TestBench.ToggleActor();
        else if (Input.GetKeyDown(KeyCode.Alpha9)) TestBench.ToggleControl();
        else if (Input.GetKeyDown(KeyCode.P)) TestBench.TogglePlant();
        else if (Input.GetKeyDown(KeyCode.K)) TestBench.CycleKnockback();
    }
}
