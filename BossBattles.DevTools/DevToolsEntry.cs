using BossBattles.Core;
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
public sealed class DevToolsEntry : MelonMod
{
    private const string Keys =
        "Ctrl+F desktop camera | Ctrl+T test actor | Ctrl+9 test actor mirror/bot | Ctrl+P plant | Ctrl+Shift+P test actor plays the newest clip | " +
        "Ctrl+K knockback | Ctrl+0 census. No headset: numpad 8/2/4/6 walk, 7/9 turn.";

    /// <summary>Orbits the battle's first actor, the test actor, or you.</summary>
    private readonly DesktopCamera _camera = new(
        () => Mod.Actors.First?.HeadPosition ?? Camera.main?.transform.position,
        message => Mod.Note("DesktopCamera", message));

    public override void OnInitializeMelon()
    {
        ModEntry? boss = ModEntry.Instance;
        if (boss == null)
        {
            LoggerInstance.Error("BossBattles is not loaded - the dev tools are off");
            return;
        }

        FlatscreenControls.Log = message => Mod.Note("Flatscreen", message);
        Mod.Note("Init", $"flatscreen controls: {FlatscreenControls.Apply(HarmonyInstance)} methods", echo: false);
        Mod.ClickCamera = () => _camera.Camera;
        SelfTest.Configure(boss.Clips, _camera.ShowFixed, _camera.CapturePng, boss.Picker, boss.NewChart);
        Mod.Say($"BossBattles.DevTools loaded. {Keys}");
    }

    public override void OnUpdate()
    {
        try
        {
            HandleKeys();
            TestBench.Update();
            FlatscreenControls.Update();
            SelfTest.Update();
        }
        catch (Exception e)
        {
            Mod.Note("DevTools.UpdateFailed", e.ToString());
        }
    }

    public override void OnLateUpdate() => _camera.LateUpdate();

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        TestBench.Forget();
        _camera.Forget();
        SelfTest.OnSceneLoaded(sceneName);
    }

    private void HandleKeys()
    {
        if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl)) return;
        var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (Input.GetKeyDown(KeyCode.F)) _camera.Toggle();
        else if (Input.GetKeyDown(KeyCode.T)) TestBench.ToggleActor();
        else if (Input.GetKeyDown(KeyCode.Alpha9)) TestBench.ToggleControl();
        else if (Input.GetKeyDown(KeyCode.P) && shift) TestBench.PlayClip(ModEntry.Instance?.Clips.All.OrderBy(c => c.Name, StringComparer.Ordinal).LastOrDefault());
        else if (Input.GetKeyDown(KeyCode.P)) TestBench.TogglePlant();
        else if (Input.GetKeyDown(KeyCode.K)) TestBench.CycleKnockback();
        else if (Input.GetKeyDown(KeyCode.Alpha0)) Mod.Report("on request");
    }
}
