using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Basalt.DevTools.Flatscreen;

/// <summary>
/// Looks around with the mouse by turning your headset, which walking already follows. A locked cursor stops
/// <c>Input.mousePosition</c> but not the Input System's delta. Alt frees the cursor, and the keys with it, for a
/// window drawn over the game; a desktop camera frees it for its own drags.
/// </summary>
internal static class MouseLook
{
    private const float DegreesPerPixel = 0.1f;
    private const float MaxPitch = 85f;
    private const int CrosshairPixels = 6;

    private static GameObject? _crosshair;
    private static bool _captured;

    /// <summary>Alt has freed the cursor.</summary>
    public static bool Paused { get; private set; }

    public static bool Captured => _captured;

    public static void Update(PlayerRig? rig, bool active, bool desktopCamera)
    {
        if (active && (Input.GetKeyDown(KeyCode.LeftAlt) || Input.GetKeyDown(KeyCode.RightAlt))) Paused = !Paused;

        var capture = active && !Paused && !desktopCamera && Application.isFocused;
        Capture(capture);

        if (capture && rig != null && Mouse.current is { } mouse) Turn(rig, mouse.delta.ReadValue() * DegreesPerPixel);
    }

    /// <summary>Turns the head by this many degrees, x right and y up, keeping it level.</summary>
    public static void Turn(PlayerRig rig, Vector2 degrees)
    {
        if (degrees == Vector2.zero) return;

        var angles = rig.Head.localEulerAngles;
        var pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, angles.x) - degrees.y, -MaxPitch, MaxPitch);
        rig.Head.localRotation = Quaternion.Euler(pitch, angles.y + degrees.x, 0f);
    }

    private static void Capture(bool capture)
    {
        if (capture && Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else if (!capture && _captured)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        _captured = capture;
        if (capture || _crosshair != null) Crosshair().SetActive(capture);
    }

    private static GameObject Crosshair()
    {
        if (_crosshair != null) return _crosshair;

        _crosshair = new GameObject("Crosshair");
        UnityEngine.Object.DontDestroyOnLoad(_crosshair);
        _crosshair.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

        var dot = new GameObject("Dot");
        dot.transform.SetParent(_crosshair.transform, false);
        dot.AddComponent<Image>().rectTransform.sizeDelta = new Vector2(CrosshairPixels, CrosshairPixels);
        return _crosshair;
    }
}
