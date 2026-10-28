using Basalt.Presentation;
using Basalt.Services.Charts;
using UnityEngine;

namespace Basalt.Arenas;

internal class ArenaVisuals(GameAssets assets)
{
    public const float LineThickness = 0.04f;
    public const float LineHeight = 0.02f;
    public const float FrameGap = 0.9f;
    public const float FrameHeight = 3f;
    public const float FrameThickness = 0.06f;
    public const float PaneThickness = 0.01f;

    private static readonly Color EdgeColour = new(1f, 0.45f, 0.1f);
    private static readonly Color EndColour = new(1f, 0.85f, 0.2f);
    private static readonly Color ZoneColour = new(0.95f, 0.95f, 0.85f);
    private static readonly Color ActorMarkerColour = new(0.25f, 0.5f, 0.95f);
    private static readonly Vector3 ActorMarkerSize = new(0.35f, 0.9f, 0.35f);

    private readonly List<(Part Part, Renderer Renderer, Material Material)> _painted = [];
    private Dictionary<Part, Color> _currentColours = [];
    private Dictionary<Part, Color> _lerpFromColours = [];
    private Dictionary<Part, Color> _lerpToColours = [];
    private float _fadeStart;
    private float _fadeSeconds;

    public enum Part
    {
        Edge,
        End,
        Zone,
        Frame,
        Pane,
        ActorMarker,
    }

    public void Colour(LaneColours colours) => _currentColours = Targets(colours);

    public void Line(Vector3 centre, Quaternion rotation, Vector3 size, Part part) =>
        Remember(Make("Basalt_Lane", centre, rotation, size, assets.Colour(_currentColours[part])), part);

    public void ActorMarker(Vector3 feet) =>
        Remember(Make("Basalt_ActorMarker", feet + (Vector3.up * ActorMarkerSize.y), Quaternion.identity, ActorMarkerSize, assets.Colour(_currentColours[Part.ActorMarker]), PrimitiveType.Cylinder), Part.ActorMarker);

    public void Pane(Vector3 centre, Quaternion rotation, Vector3 size) =>
        Remember(Make("Basalt_Pane", centre, rotation, size, assets.Glass(_currentColours[Part.Pane])), Part.Pane);

    public void Recolour(LaneColours colours, float seconds)
    {
        if (_painted.Count == 0) return;

        _lerpFromColours = new Dictionary<Part, Color>(_currentColours);
        _lerpToColours = Targets(colours);
        _fadeStart = Time.time;
        _fadeSeconds = seconds;
        Tick();
    }

    public void Tick()
    {
        if (_lerpToColours.Count == 0) return;

        var k = _fadeSeconds <= 0f ? 1f : Mathf.Clamp01((Time.time - _fadeStart) / _fadeSeconds);
        foreach ((var part, var target) in _lerpToColours)
        {
            var from = _lerpFromColours.TryGetValue(part, out var f) ? f : target;
            var to = target;

            // A pane fading in or out keeps its hue and only changes opacity.
            if (part == Part.Pane && from.a <= 0f) from = new Color(to.r, to.g, to.b, 0f);
            if (part == Part.Pane && to.a <= 0f) to = new Color(from.r, from.g, from.b, 0f);
            _currentColours[part] = Color.Lerp(from, to, k);
        }

        foreach ((var part, var renderer, var material) in _painted.Where(p => p.Renderer != null && p.Material != null))
        {
            var colour = _currentColours[part];
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
            if (material.HasProperty("_Color")) material.SetColor("_Color", colour);
            renderer.enabled = part != Part.Pane || colour.a > 0.004f;
        }

        if (k >= 1f) _lerpToColours = [];
    }

    private static Dictionary<Part, Color> Targets(LaneColours colours)
    {
        var edge = PickColour(colours.Edges, EdgeColour);
        return new Dictionary<Part, Color>
        {
            [Part.Edge] = edge,
            [Part.End] = PickColour(colours.Ends, EndColour),
            [Part.Zone] = PickColour(colours.Zone, ZoneColour),
            [Part.Frame] = PickColour(colours.Backstop, edge),
            [Part.Pane] = PickColour(colours.Walls, new Color(0f, 0f, 0f, 0f)),
            [Part.ActorMarker] = ActorMarkerColour,
        };

        static Color PickColour(Rgba? chart, Color fallback)
        {
            return chart is { } c ? new Color(c.R, c.G, c.B, c.A) : fallback;
        }
    }

    private static GameObject Make(string name, Vector3 centre, Quaternion rotation, Vector3 size, Material? material, PrimitiveType shape = PrimitiveType.Cube)
    {
        var cube = GameHelpers.Shape(shape, name, centre, rotation, size);
        UnityEngine.Object.Destroy(cube.GetComponent<Collider>());
        if (material != null) cube.GetComponent<Renderer>().material = material;
        return cube;
    }

    private void Remember(GameObject cube, Part part)
    {
        var renderer = cube.GetComponent<Renderer>();
        _painted.Add((part, renderer, renderer.material));
        if (part == Part.Pane && _currentColours[Part.Pane].a <= 0.004f) renderer.enabled = false;
    }
    public void Destroy()
    {
        foreach ((_, var renderer, _) in _painted.Where(p => p.Renderer != null)) UnityEngine.Object.Destroy(renderer.gameObject);
        Forget();
    }

    public void Forget()
    {
        _painted.Clear();
        _lerpToColours = [];
    }
}
