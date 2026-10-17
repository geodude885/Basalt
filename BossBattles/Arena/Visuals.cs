using BossBattles.Presentation;
using BossBattles.Services.Charts;
using UnityEngine;

namespace BossBattles.Arena;

/// <summary>What the lane looks like: its lines, the backstop's frame and the wall panes, in colours a phase can fade between.</summary>
internal class Visuals
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

    private readonly List<(Part Part, Renderer Renderer, Material Material)> _painted = [];
    private Dictionary<Part, Color> _now = [];
    private Dictionary<Part, Color> _from = [];
    private Dictionary<Part, Color> _to = [];
    private float _fadeStart;
    private float _fadeSeconds;

    /// <summary>The parts a chart colours, so a phase can recolour them.</summary>
    public enum Part
    {
        Edge,
        End,
        Zone,
        Frame,
        Pane,
    }

    /// <summary>The colours parts are painted in from now on, at once.</summary>
    public void Colour(LaneColours colours) => _now = Targets(colours);

    /// <summary>A painted line nothing collides with.</summary>
    public void Line(Vector3 centre, Quaternion rotation, Vector3 size, Part part) =>
        Remember(Make("BossBattles_Lane", centre, rotation, size, Paint.Colour(_now[part])), part);

    /// <summary>A see-through pane, hidden while the walls' colour is clear.</summary>
    public void Pane(Vector3 centre, Quaternion rotation, Vector3 size) =>
        Remember(Make("BossBattles_Pane", centre, rotation, size, Paint.Glass(_now[Part.Pane])), Part.Pane);

    /// <summary>Fades the lane to <paramref name="colours"/> over <paramref name="seconds"/>; 0 is at once.</summary>
    public void Recolour(LaneColours colours, float seconds)
    {
        if (_painted.Count == 0) return;

        _from = new Dictionary<Part, Color>(_now);
        _to = Targets(colours);
        _fadeStart = Time.time;
        _fadeSeconds = seconds;
        Tick();
    }

    public void Tick()
    {
        if (_to.Count == 0) return;

        var k = _fadeSeconds <= 0f ? 1f : Mathf.Clamp01((Time.time - _fadeStart) / _fadeSeconds);
        foreach ((Part part, Color target) in _to)
        {
            Color from = _from.TryGetValue(part, out Color f) ? f : target;
            Color to = target;

            // A pane fading in or out keeps its hue and only changes opacity.
            if (part == Part.Pane && from.a <= 0f) from = new Color(to.r, to.g, to.b, 0f);
            if (part == Part.Pane && to.a <= 0f) to = new Color(from.r, from.g, from.b, 0f);
            _now[part] = Color.Lerp(from, to, k);
        }

        foreach ((Part part, Renderer renderer, Material material) in _painted.Where(p => p.Renderer != null && p.Material != null))
        {
            Color colour = _now[part];
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
            if (material.HasProperty("_Color")) material.SetColor("_Color", colour);
            renderer.enabled = part != Part.Pane || colour.a > 0.004f;
        }

        if (k >= 1f) _to = [];
    }

    private static Dictionary<Part, Color> Targets(LaneColours colours)
    {
        Color edge = Pick(colours.Edges, EdgeColour);
        return new Dictionary<Part, Color>
        {
            [Part.Edge] = edge,
            [Part.End] = Pick(colours.Ends, EndColour),
            [Part.Zone] = Pick(colours.Zone, ZoneColour),
            [Part.Frame] = Pick(colours.Backstop, edge),
            [Part.Pane] = Pick(colours.Walls, new Color(0f, 0f, 0f, 0f)),
        };

        static Color Pick(Rgba? chart, Color fallback)
        {
            return chart is { } c ? new Color(c.R, c.G, c.B, c.A) : fallback;
        }
    }

    private static GameObject Make(string name, Vector3 centre, Quaternion rotation, Vector3 size, Material? material)
    {
        GameObject cube = GameHelpers.Cube(name, centre, rotation, size);
        UnityEngine.Object.Destroy(cube.GetComponent<Collider>());
        if (material != null) cube.GetComponent<Renderer>().material = material;
        return cube;
    }

    private void Remember(GameObject cube, Part part)
    {
        Renderer renderer = cube.GetComponent<Renderer>();
        _painted.Add((part, renderer, renderer.material));
        if (part == Part.Pane && _now[Part.Pane].a <= 0.004f) renderer.enabled = false;
    }
    public void Destroy()
    {
        foreach ((_, Renderer renderer, _) in _painted.Where(p => p.Renderer != null)) UnityEngine.Object.Destroy(renderer.gameObject);
        Forget();
    }

    public void Forget()
    {
        _painted.Clear();
        _to = [];
    }
}
