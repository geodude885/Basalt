using BossBattles.DevTools.Flatscreen;
using BossBattles.DevTools.Testing;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(BossBattles.DevTools.DevToolsEntry), "BossBattles.DevTools", "1.0.0", "ACutiePi")]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]
[assembly: MelonAdditionalDependencies("BossBattles")]
[assembly: HarmonyDontPatchAll]

namespace BossBattles.DevTools;

/// <summary>Developer tools for BossBattles: the self-tests, a keyboard-driven test actor, and flatscreen play.</summary>
public class DevToolsEntry : MelonMod
{
    private const string Keys =
        "Ctrl+F desktop camera | Ctrl+T test actor | Ctrl+9 test actor mirror/bot | Ctrl+P plant | " +
        "Ctrl+K knockback. Click presses picker buttons. No headset: numpad 8/2/4/6 walk, 7/9 turn.";

    /// <summary>Orbits the battle's first actor, the test actor, or you.</summary>
    private readonly DesktopCamera _camera = new(
        () => Mod.Actors.All.FirstOrDefault(a => a.Exists)?.HeadPosition ?? Camera.main?.transform.position,
        message => Logger.Info($"[DesktopCamera] {message}"));

    public override void OnInitializeMelon()
    {
        if (Mod.Picker is null)
        {
            LoggerInstance.Error("BossBattles is not loaded - the dev tools are off");
            return;
        }

        FlatscreenControls.Log = message => Logger.Info($"[Flatscreen] {message}");
        Logger.Debug($"[Init] flatscreen controls: {FlatscreenControls.Apply(HarmonyInstance)} methods");
        SelfTest.Configure(_camera.ShowFixed, _camera.CapturePng);
        Logger.Info($"BossBattles.DevTools loaded. {Keys}");
    }

    public override void OnUpdate()
    {
        try
        {
            HandleKeys();
            HandleClick();
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

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        TestBench.Forget();
        _camera.Forget();
        SelfTest.OnSceneLoaded(sceneName);
    }

    /// <summary>A mouse click presses the picker button under the cursor, through the desktop camera when it shows.</summary>
    private void HandleClick()
    {
        if (!Input.GetMouseButtonDown(0) || Mod.Picker is not { } picker || (_camera.Camera ?? Camera.main) is not { } camera) return;

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
