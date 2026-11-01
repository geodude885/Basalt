using Il2CppRUMBLE.Interactions.InteractionBase;
using Il2CppRUMBLE.Players;
using UnityEngine;

namespace Basalt.DevTools.Flatscreen;

/// <summary>
/// Pushes a game button with a stand-in finger, so its own sound, toggles and wiring all run. A button only reacts to
/// a collider on the finger layer with the finger tag, and only fires for a hand it was told is reaching for it; without
/// a headset the real fingers stay where they are whatever the hand transform does.
/// </summary>
internal sealed class ButtonPusher
{
    private const string FingerTag = "Invoke_InteractionButton";
    private const int FingerLayer = 31;
    private const float FingerRadius = 0.02f;
    private const float Approach = 0.12f;
    private const float Overshoot = 0.02f;
    private const float PushSeconds = 0.45f;
    private const float HoldSeconds = 0.45f;
    private const float ReleaseSeconds = 0.8f;
    private const float Reach = 50f;

    private InteractionButton? _button;
    private InteractionHand? _hand;
    private GameObject? _finger;
    private Rigidbody? _body;
    private Vector3 _from;
    private Vector3 _to;
    private float _elapsed;

    public bool Pushing => _finger != null;

    /// <summary>The nearest game button along a ray, looking past anything else on the way, like your own colliders.</summary>
    public static InteractionButton? ButtonOn(Ray ray) =>
        Physics.RaycastAll(ray, Reach, Physics.AllLayers, QueryTriggerInteraction.Collide)
            .OrderBy(h => h.distance)
            .Select(h => h.collider.GetComponentInParent<InteractionButton>())
            .FirstOrDefault(b => b != null);

    public void Push(InteractionButton button, PlayerController player)
    {
        if (Pushing) return;

        var hand = player.PlayerVR.RightController.InteractionHand;
        button.StartPreInteraction(hand);

        var along = (button.RailsStart - button.RailsEnd).normalized;
        (_button, _hand, _from, _to, _elapsed) = (button, hand, button.RailsStart + (along * Approach), button.RailsEnd - (along * Overshoot), 0f);

        _finger = new GameObject("BasaltFinger") { layer = FingerLayer, tag = FingerTag };
        var sphere = _finger.AddComponent<SphereCollider>();
        sphere.radius = FingerRadius;
        sphere.isTrigger = true;
        _body = _finger.AddComponent<Rigidbody>();
        _body.isKinematic = true;
        MoveFinger(_from);
    }

    public void Update()
    {
        if (!Pushing) return;

        // Gone with a scene, or a button that hid itself when pressed.
        if (_button == null || !_button.gameObject.activeInHierarchy)
        {
            Finish();
            return;
        }

        _elapsed += Time.deltaTime;
        if (_elapsed >= PushSeconds + HoldSeconds + ReleaseSeconds) Finish();
        else if (_elapsed < PushSeconds + HoldSeconds) MoveFinger(Vector3.Lerp(_from, _to, _elapsed / PushSeconds));
        else MoveFinger(_from);
    }

    private void MoveFinger(Vector3 at)
    {
        _finger!.transform.position = at;
        _body!.position = at;
    }

    private void Finish()
    {
        if (_button != null) _button.StopPreInteraction(_hand);
        if (_finger != null) UnityEngine.Object.Destroy(_finger);
        (_button, _hand, _finger, _body) = (null, null, null, null);
    }
}
