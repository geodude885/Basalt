using UnityEngine;

namespace BossBattles.Flatscreen;

/// <summary>
/// A desktop orbit camera for watching the game without a headset - or as a spectator view in the
/// game window while in VR. Self-contained so it can move to its own mod: it knows nothing of the
/// lab, takes what to orbit and where to log from its host, and owns its mouse controls.
/// <list type="bullet">
/// <item>Right mouse drag: orbit. Scroll: zoom. Middle mouse drag: pan away from the focus.</item>
/// <item>The host calls <see cref="Toggle"/> (from its own key binding), <see cref="LateUpdate"/>
/// once per frame, and <see cref="Forget"/> on a scene change.</item>
/// </list>
/// <para>
/// It is a separate camera rather than the game's headset camera moved around - moving that one
/// would fight the player rig and tracking. It renders only to the desktop window
/// (<c>stereoTargetEye = None</c>), above the game's camera, copying its field of view and culling.
/// </para>
/// </summary>
public sealed class DesktopCamera
{
    private const float OrbitDegreesPerPixel = 0.25f;
    private const float ZoomPerScrollStep = 0.15f;
    private const float PanPerPixel = 0.0025f;
    private const float MinDistance = 0.75f;
    private const float MaxDistance = 25f;
    private const float MinPitch = -80f;
    private const float MaxPitch = 85f;
    private const float MaxDragPixelsPerFrame = 200f;

    private readonly Func<Vector3?> _focus;
    private readonly Action<string> _log;

    private Camera? _camera;
    private float _yaw = 180f;
    private float _pitch = 15f;
    private float _distance = 4f;
    private Vector3 _panOffset;
    private Vector3 _lastFocus;
    private Vector3 _lastMouse;
    private bool _fixed;

    /// <param name="focus">The point to orbit, or null to keep orbiting the last one.</param>
    /// <param name="log">Where to report state changes.</param>
    public DesktopCamera(Func<Vector3?> focus, Action<string> log)
    {
        _focus = focus;
        _log = log;
    }

    public bool Active => _camera != null && _camera.enabled;

    /// <summary>The camera while it is on - for a host turning mouse clicks into rays.</summary>
    public Camera? Camera => Active ? _camera : null;

    public void Toggle()
    {
        if (Active && !_fixed)
        {
            _camera!.enabled = false;
            _log("desktop camera off");
            return;
        }

        if (_camera == null) _camera = Create();
        if (_camera == null) return;

        _fixed = false;
        _panOffset = Vector3.zero;
        _lastMouse = Input.mousePosition;
        _camera.enabled = true;
        _log("desktop camera on - right drag orbit, scroll zoom, middle drag pan");
    }

    /// <summary>
    /// Turns the camera on at a fixed viewpoint - for screenshots, or a host's own camera shots.
    /// Mouse control is off until <see cref="Toggle"/> is used.
    /// </summary>
    public void ShowFixed(Vector3 position, Vector3 lookAt)
    {
        if (_camera == null) _camera = Create();
        if (_camera == null) return;

        _fixed = true;
        _camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position));
        _camera.enabled = true;
    }

    /// <summary>
    /// Renders the camera's current view to a PNG of the given size, or null if it is off. Renders
    /// through a RenderTexture rather than <c>ScreenCapture.CaptureScreenshot</c>, whose interop
    /// string marshalling throws MissingMethodException in this MelonLoader build.
    /// </summary>
    public byte[]? CapturePng(int width, int height)
    {
        if (!Active) return null;

        RenderTexture target = new(width, height, 24);
        RenderTexture? previousTarget = _camera!.targetTexture;
        RenderTexture? previousActive = RenderTexture.active;

        try
        {
            _camera.targetTexture = target;
            _camera.Render();

            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();

            byte[] png = ImageConversion.EncodeToPNG(image);
            UnityEngine.Object.Destroy(image);
            return png;
        }
        finally
        {
            _camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            target.Release();
            UnityEngine.Object.Destroy(target);
        }
    }

    /// <summary>After the game's own LateUpdates, so the camera follows the final pose of its focus.</summary>
    public void LateUpdate()
    {
        if (!Active || _fixed) return;

        Vector3 mouse = Input.mousePosition;
        Vector3 delta = mouse - _lastMouse;
        _lastMouse = mouse;

        // A jump this size is the cursor re-entering the window or focus coming back, not a drag.
        if (delta.sqrMagnitude > MaxDragPixelsPerFrame * MaxDragPixelsPerFrame) delta = Vector3.zero;

        var panning = Input.GetMouseButton(2);

        if (Input.GetMouseButton(1))
        {
            _yaw += delta.x * OrbitDegreesPerPixel;
            _pitch = Mathf.Clamp(_pitch - (delta.y * OrbitDegreesPerPixel), MinPitch, MaxPitch);
        }

        // Pressing the wheel to pan nudges it, which zoomed mid-pan - the jank.
        var scroll = panning ? 0f : Input.mouseScrollDelta.y;
        if (scroll != 0f) _distance = Mathf.Clamp(_distance * (1f - (scroll * ZoomPerScrollStep)), MinDistance, MaxDistance);

        var orbit = Quaternion.Euler(_pitch, _yaw, 0f);

        if (panning)
        {
            _panOffset += (orbit * Vector3.left * (delta.x * PanPerPixel * _distance))
                          + (orbit * Vector3.down * (delta.y * PanPerPixel * _distance));
        }

        _lastFocus = _focus() ?? _lastFocus;
        Vector3 target = _lastFocus + _panOffset;

        Transform transform = _camera!.transform;
        transform.position = target - (orbit * Vector3.forward * _distance);
        transform.rotation = orbit;
    }

    /// <summary>For a scene change: a camera kept across scenes (DontDestroyOnLoad) only needs its focus reset.</summary>
    public void Forget() => _panOffset = Vector3.zero;

    private Camera? Create()
    {
        Camera? game = Camera.main;

        var holder = new GameObject("DesktopCamera");
        UnityEngine.Object.DontDestroyOnLoad(holder);

        Camera camera = holder.AddComponent<Camera>();
        camera.stereoTargetEye = StereoTargetEyeMask.None;
        camera.fieldOfView = 70f;
        camera.nearClipPlane = 0.05f;

        if (game != null)
        {
            camera.cullingMask = game.cullingMask;
            camera.farClipPlane = game.farClipPlane;
            camera.depth = game.depth + 1f;
            camera.clearFlags = game.clearFlags;
            camera.backgroundColor = game.backgroundColor;
            _lastFocus = game.transform.position;
        }
        else
        {
            camera.depth = 100f;
        }

        _log($"desktop camera created over {(game == null ? "no main camera" : game.name)}");
        return camera;
    }
}
