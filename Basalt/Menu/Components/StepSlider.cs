using Basalt.Presentation;
using Il2CppTMPro;
using UnityEngine;

namespace Basalt.Menu.Components;

// The game's slider standing upright beside the card, a label by each step. Rebuilt only when its steps change, so
// it is never torn down under a hand moving it.
internal class StepSlider(Transform menu, GameAssets assets)
{
    // Right of the card's edge.
    private const float SliderX = 0.56f;

    private string _key = "";

    public GameObject? Root { get; private set; }

    public MenuSlider? Spec { get; private set; }

    public void Show(MenuSlider? spec)
    {
        Spec = spec;
        var key = spec == null ? "" : $"{spec.Id}:{string.Join(",", spec.Labels)}";
        if (key == _key) return;

        if (Root != null) UnityEngine.Object.Destroy(Root);
        Root = spec == null ? null : Build(spec);
        _key = key;
    }

    private GameObject Build(MenuSlider spec)
    {
        var holder = new GameObject("Slider");
        holder.transform.SetParent(menu, false);
        holder.transform.localPosition = new Vector3(SliderX, 0.17f, -0.02f);

        var slider = assets.CreateGameSlider(holder.transform, spec.Labels.Count, spec.Step, step => Spec?.Choose(step))!;

        // Upright, its handle standing out towards you, and its first step at the top; then centred on its spot.
        slider.transform.localRotation = Quaternion.Euler(0f, 0f, 90f) * slider.transform.localRotation;
        var grip = slider.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.Squash() == "handleparent");
        if (grip != null && holder.transform.InverseTransformPoint(grip.position).z > 0f)
            slider.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * slider.transform.localRotation;
        if (StepEnds(slider, holder.transform) is { } ends && ends.First.y < ends.Last.y)
            slider.transform.localRotation = Quaternion.Euler(0f, 0f, 180f) * slider.transform.localRotation;
        if (StepEnds(slider, holder.transform) is { } track)
            slider.transform.localPosition -= new Vector3(track.First.x, (track.First.y + track.Last.y) / 2f, 0f);

        if (StepEnds(slider, holder.transform) is not { } placed) return holder;

        foreach ((var label, var i) in spec.Labels.Select((label, i) => (label, i)))
        {
            var at = Vector3.Lerp(placed.First, placed.Last, spec.Labels.Count == 1 ? 0f : i / (float)(spec.Labels.Count - 1));
            var text = assets.CreateText(holder.transform, new Vector3(at.x + 0.17f, at.y, -0.01f), new Vector2(0.16f, 0.05f), 0.5f, GameAssets.TextOnRock);
            text.enableAutoSizing = true;
            text.fontSizeMin = 0.2f;
            text.fontSizeMax = 0.5f;
            text.alignment = TextAlignmentOptions.Left;
            text.text = label;
        }

        return holder;
    }

    // Where the slider's first and last steps are, in the holder's space.
    private static (Vector3 First, Vector3 Last)? StepEnds(GameObject slider, Transform holder)
    {
        var start = slider.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.Squash() == "startpoint");
        var end = slider.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.Squash() == "endpoint");
        return start == null || end == null ? null : (holder.InverseTransformPoint(start.position), holder.InverseTransformPoint(end.position));
    }
}
