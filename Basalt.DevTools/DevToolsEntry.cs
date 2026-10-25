using Basalt.Battle;
using Basalt.DevTools.Flatscreen;
using Basalt.DevTools.Testing;
using Il2CppRUMBLE.Players.Subsystems;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.EventSystems;

[assembly: MelonInfo(typeof(Basalt.DevTools.DevToolsEntry), "Basalt.DevTools", "1.0.0", "ACutiePi")]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]
[assembly: MelonAdditionalDependencies("Basalt")]
[assembly: HarmonyDontPatchAll]

namespace Basalt.DevTools;

/// <summary>Developer tools for Basalt: the self-tests, a keyboard-driven test actor, and flatscreen play.</summary>
public class DevToolsEntry : MelonMod
{
    private const string Keys =
        "Ctrl+F desktop camera | Ctrl+T test actor | Ctrl+9 test actor mirror/bot | Ctrl+P plant | " +
        "Ctrl+K knockback. Click presses picker buttons. No headset: numpad 8/2/4/6 walk, 7/9 turn.";

    /// <summary>Orbits the battle's first actor, the test actor, or you.</summary>
    private readonly DesktopCamera _camera = new(
        () => Mod.Actors.Actors.FirstOrDefault(a => a.Ready)?.Body?.Rig.Head.position ?? Camera.main?.transform.position,
        message => Logger.Info($"[DesktopCamera] {message}"));

    private MelonPreferences_Entry<bool> _invincibleInCharts = null!;

    public override void OnInitializeMelon()
    {
        if (Mod.Picker is null)
        {
            LoggerInstance.Error("Basalt is not loaded - the dev tools are off");
            return;
        }

        FlatscreenControls.Log = message => Logger.Info($"[Flatscreen] {message}");
        Logger.Debug($"[Init] flatscreen controls: {FlatscreenControls.Apply(HarmonyInstance)} methods");
        SelfTest.Configure(_camera.ShowFixed, _camera.CapturePng);

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
            HandleClick();
            KeepInvincibleInCharts();
            TestBench.Update();
            FlatscreenControls.Update();
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
        if (!_invincibleInCharts.Value || Mod.Battle.State != BattleState.Running || Mod.Battle.Mode == BattleMode.Record) return;
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

    // A mouse click presses the picker button under the cursor, through the desktop camera when it shows. A click on
    // a window drawn over the game (UIFramework's settings) is that window's, and must not reach the picker behind it.
    private void HandleClick()
    {
        if (!Input.GetMouseButtonDown(0) || EventSystem.current?.IsPointerOverGameObject() == true) return;
        if (Mod.Picker is not { } picker || (_camera.Camera ?? Camera.main) is not { } camera) return;

        var ray = camera.ScreenPointToRay(Input.mousePosition);
        var unitBox = new Bounds(Vector3.zero, Vector3.one);
        var hit = picker.ShownButtons
            .Select(b => (b.Id, Hit: unitBox.IntersectRay(new Ray(b.Face.InverseTransformPoint(ray.origin), b.Face.InverseTransformVector(ray.direction)), out var distance), Distance: distance))
            .Where(b => b.Hit)
            .OrderBy(b => b.Distance)
            .Select(b => b.Id)
            .FirstOrDefault();

        if (hit != null) picker.Press(hit);
    }

    private void HandleKeys()
    {
        if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl)) return;

        if (Input.GetKeyDown(KeyCode.F)) _camera.Toggle();
        else if (Input.GetKeyDown(KeyCode.T)) TestBench.ToggleActor();
        else if (Input.GetKeyDown(KeyCode.Alpha9)) TestBench.ToggleControl();
        else if (Input.GetKeyDown(KeyCode.P)) TestBench.TogglePlant();
        else if (Input.GetKeyDown(KeyCode.K)) TestBench.CycleKnockback();
    }
}
