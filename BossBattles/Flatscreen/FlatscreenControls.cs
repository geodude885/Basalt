using HarmonyLib;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using UnityEngine;
using UnityEngine.XR;

namespace BossBattles.Flatscreen;

/// <summary>
/// Walk and turn your own player from the numpad when there is no headset: 8/2/4/6 walk
/// (relative to where your head faces), 7/9 snap turn. Only while <c>XRSettings.isDeviceActive</c>
/// is false, so in VR your controllers are never overridden.
/// <para>
/// Self-contained like the rest of <c>Flatscreen/</c>: the host calls <see cref="Apply"/> once,
/// <see cref="Update"/> every frame, and may set <see cref="Log"/>.
/// </para>
/// </summary>
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
        foreach (Type patch in typeof(FlatscreenControls).GetNestedTypes())
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

    [HarmonyPatch(typeof(PlayerMovement), nameof(PlayerMovement.ProcessInput))]
    public static class LocalSticks
    {
        public static bool Prefix(PlayerMovement __instance)
        {
            try
            {
                if (!Enabled || !IsFlatscreen) return true;

                PlayerController? you = PlayerManager.instance?.localPlayer?.Controller;
                if (you == null || __instance.ParentController == null || __instance.ParentController.Pointer != you.Pointer) return true;

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
    }
}
