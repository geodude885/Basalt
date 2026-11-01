using Basalt.Arenas;
using Basalt.Battle;
using Basalt.DevTools.Testing;
using HarmonyLib;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR;

namespace Basalt.DevTools.Flatscreen;

/// <summary>
/// Plays your player when no headset is active: the mouse looks, WASD walks, Q/E snap-turn, the move keys run moves
/// and a click pushes the game button in the crosshair. Also drives the test actor.
/// </summary>
public static class FlatscreenControls
{
    private const string ControlsHelp = "mouse looks, WASD walks, Q/E turn, click pushes buttons, Alt frees the cursor; Space, Shift, 1-4, Z/X/C/V, T/G run moves";
    private static readonly ButtonPusher Pusher = new();

    private static Vector2 _walk;
    private static Vector2 _turn;
    private static bool? _reportedFlatscreen;

    public static Action<string> Log { get; set; } = _ => { };

    /// <summary>The host can switch the controls off; they are on by default but only act without a headset.</summary>
    public static bool Enabled { get; set; } = true;

    public static bool IsFlatscreen => !XRSettings.isDeviceActive;

    /// <summary>A walk stick set by code (x right, y forward) instead of the keys - a host's own tests; null for the keys.</summary>
    public static Vector2? ScriptedWalk { get; set; }

    /// <summary>A turn stick set by code (x right) instead of the keys; null for the keys.</summary>
    public static Vector2? ScriptedTurn { get; set; }

    /// <summary>Whether a button push is still under way.</summary>
    internal static bool Pushing => Pusher.Pushing;

    /// <summary>Patches each nested patch class on its own and returns how many methods were patched.</summary>
    public static int Apply(HarmonyLib.Harmony harmony)
    {
        var total = 0;
        foreach (var patch in typeof(FlatscreenControls).GetNestedTypes())
        {
            try
            {
                total += harmony.CreateClassProcessor(patch).Patch()?.Count ?? 0;
            }
            catch (Exception e)
            {
                Log($"patching {patch.Name} failed: {e.Message}");
            }
        }

        return total;
    }

    /// <param name="desktopCamera">The desktop camera while it is on: it takes the mouse, and clicks go through it.</param>
    public static void Update(Camera? desktopCamera)
    {
        var flatscreen = IsFlatscreen;
        if (_reportedFlatscreen != flatscreen)
        {
            _reportedFlatscreen = flatscreen;
            Log(flatscreen ? $"no headset - {ControlsHelp}" : "headset active - flatscreen controls off");
        }

        Pusher.Update();
        var controller = BattlePlayer.Player?.Controller;

        // The self-tests turn and walk you themselves, and a nudged mouse must not spoil them.
        var playing = Enabled && flatscreen && !SelfTest.Enabled;
        MouseLook.Update(controller == null ? null : PlayerRig.From(controller), playing, desktopCamera != null);
        HandleClick(desktopCamera);

        if (!Enabled || !flatscreen)
        {
            _walk = Vector2.zero;
            _turn = Vector2.zero;
            return;
        }

        // The keys are the test actor's while it is in bot control, and a window's while the cursor is free.
        var keys = playing && !MouseLook.Paused && TestBench.Sticks == null;
        _walk = ScriptedWalk ?? (keys ? FlatscreenKeys.Walk : Vector2.zero);
        _turn = ScriptedTurn ?? (keys ? FlatscreenKeys.Turn : Vector2.zero);

        if (!keys || controller == null) return;
        foreach (var move in FlatscreenKeys.PressedMoves) RunMove(controller, move);
    }

    /// <summary>Runs one of your moves if the game would let you now: a chart can withhold it, and it can be cooling down.</summary>
    internal static bool RunMove(PlayerController controller, string name)
    {
        if (Maps.IsCurrentlyOnline) return false;

        var processor = controller.PlayerProcessor;
        var stack = processor.availableStacks.ToArray().FirstOrDefault(s => string.Equals(s?.CachedName, name, StringComparison.OrdinalIgnoreCase));
        if (stack == null || !stack.CanExecuteStack(new IProcessor(processor.Pointer))) return false;

        processor.Execute(stack);
        return true;
    }

    /// <summary>Pushes the game button along a ray; false when there is none, or a push is still under way.</summary>
    internal static bool Click(Ray ray)
    {
        if (Pusher.Pushing || Maps.IsCurrentlyOnline || BattlePlayer.Player?.Controller is not { } controller) return false;
        if (ButtonPusher.ButtonOn(ray) is not { } button) return false;

        Pusher.Push(button, controller);
        return true;
    }

    // From the crosshair, or under the free cursor. A click on a window drawn over the game (UIFramework's settings) is that window's.
    private static void HandleClick(Camera? desktopCamera)
    {
        if (!Input.GetMouseButtonDown(0) || (desktopCamera ?? Camera.main) is not { } camera) return;
        if (!MouseLook.Captured && EventSystem.current?.IsPointerOverGameObject() == true) return;

        var screenPoint = MouseLook.Captured ? new Vector3(Screen.width / 2f, Screen.height / 2f) : Input.mousePosition;
        Click(camera.ScreenPointToRay(screenPoint));
    }

    /// <summary>The keys for you without a headset, and for the test actor in bot control.</summary>
    [HarmonyPatch(typeof(PlayerMovement), nameof(PlayerMovement.ProcessInput))]
    public static class Sticks
    {
        private static bool _throwLogged;

        public static bool Prefix(PlayerMovement __instance)
        {
            try
            {
                if (__instance.ParentController is not { } controller) return true;

                if (TestBench.Sticks is { } test && TestBench.Actor?.Body?.Controller is { } actor && actor.Pointer == controller.Pointer)
                {
                    MovementInput.Apply(__instance, test.Walk, test.Turn);
                    return false;
                }

                if (!Enabled || !IsFlatscreen) return true;

                var playerController = BattlePlayer.Player?.Controller;
                if (playerController == null || controller.Pointer != playerController.Pointer) return true;

                MovementInput.Apply(__instance, _walk, _turn);
                return false;
            }
            catch (Exception e)
            {
                // Fail open: vanilla behaviour, and stop trying for the session.
                Enabled = false;
                Log($"flatscreen movement failed and is off for this session: {e}");
                return true;
            }
        }

        /// <summary>
        /// Without a headset the game's ProcessInput throws every frame, and once patched the interop
        /// trampoline reports each one: suppressed, the first logged.
        /// </summary>
        public static Exception? Finalizer(Exception? __exception)
        {
            if (__exception == null || _throwLogged) return null;

            _throwLogged = true;
            Log($"PlayerMovement.ProcessInput throws - expected without a headset; suppressed: {__exception.Message}");
            return null;
        }
    }
}
