using UnityEngine;

namespace Basalt.DevTools.Flatscreen;

/// <summary>
/// A desktop orbit camera for watching without a headset, or spectating in VR: right drag orbits,
/// scroll zooms, middle drag pans. A camera of its own, rendering only to the desktop window, so it
/// never fights the player rig.
/// </summary>
public class DesktopCamera
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

    /// <summary>Turns the camera on at a fixed viewpoint, mouse control off until <see cref="Toggle"/>.</summary>
    public void ShowFixed(Vector3 position, Vector3 lookAt)
    {
        if (_camera == null) _camera = Create();
        if (_camera == null) return;

        _fixed = true;
        _camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position));
        _camera.enabled = true;
    }

    /// <summary>
    /// The camera's view as a PNG, or null when it is off. Through a RenderTexture: the interop's
    /// <c>ScreenCapture.CaptureScreenshot</c> throws MissingMethodException.
    /// </summary>
    public byte[]? CapturePng(int width, int height)
    {
        if (!Active) return null;

        RenderTexture target = new(width, height, 24);
        var previousTarget = _camera!.targetTexture;
        var previousActive = RenderTexture.active;

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

        var mouse = Input.mousePosition;
        var delta = mouse - _lastMouse;
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
        var target = _lastFocus + _panOffset;

        var transform = _camera!.transform;
        transform.position = target - (orbit * Vector3.forward * _distance);
        transform.rotation = orbit;
    }

    /// <summary>For a scene change: a camera kept across scenes (DontDestroyOnLoad) only needs its focus reset.</summary>
    public void Forget() => _panOffset = Vector3.zero;

    private Camera? Create()
    {
        var game = Camera.main;

        var holder = new GameObject("DesktopCamera");
        UnityEngine.Object.DontDestroyOnLoad(holder);

        var camera = holder.AddComponent<Camera>();
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
