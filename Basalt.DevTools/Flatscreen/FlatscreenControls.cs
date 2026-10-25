using Basalt.Battle;
using Basalt.DevTools.Testing;
using HarmonyLib;
using Il2CppRUMBLE.Players.Subsystems;
using UnityEngine;
using UnityEngine.XR;

namespace Basalt.DevTools.Flatscreen;

/// <summary>Walks (numpad 8/2/4/6) and snap-turns (7/9) your player when no headset is active, and drives the test actor.</summary>
public static class FlatscreenControls
{
    private static Vector2 _walk;
    private static Vector2 _turn;
    private static bool? _reportedFlatscreen;

    public static Action<string> Log { get; set; } = _ => { };

    /// <summary>The host can switch the numpad off; it is on by default but only acts without a headset.</summary>
    public static bool Enabled { get; set; } = true;

    public static bool IsFlatscreen => !XRSettings.isDeviceActive;

    /// <summary>A walk stick set by code (x right, y forward) instead of the numpad - a host's own tests; null for the keys.</summary>
    public static Vector2? ScriptedWalk { get; set; }

    /// <summary>A turn stick set by code (x right) instead of the numpad; null for the keys.</summary>
    public static Vector2? ScriptedTurn { get; set; }

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

    public static void Update()
    {
        var flatscreen = IsFlatscreen;
        if (_reportedFlatscreen != flatscreen)
        {
            _reportedFlatscreen = flatscreen;
            Log(flatscreen
                ? "no headset - numpad walks (8/2/4/6) and turns (7/9) your player"
                : "headset active - numpad controls off");
        }

        if (!Enabled || !flatscreen)
        {
            _walk = Vector2.zero;
            _turn = Vector2.zero;
            return;
        }

        _walk = ScriptedWalk ?? new Vector2(Axis(KeyCode.Keypad6, KeyCode.Keypad4), Axis(KeyCode.Keypad8, KeyCode.Keypad2));
        _turn = ScriptedTurn ?? new Vector2(Axis(KeyCode.Keypad9, KeyCode.Keypad7), 0f);
    }

    private static float Axis(KeyCode positive, KeyCode negative) =>
        (Input.GetKey(positive) ? 1f : 0f) - (Input.GetKey(negative) ? 1f : 0f);

    /// <summary>The numpad for you without a headset, and the keys for the test actor in bot control.</summary>
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
                Log($"numpad movement failed and is off for this session: {e}");
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
