using BossBattles.Core;
using BossBattles.Services.Charts;
using Il2CppRUMBLE.MoveSystem;
using UnityEngine;

namespace BossBattles.Battle;

/// <summary>
/// The lane on the floor - edge lines and end lines - and its walls, all invisible: the backstop
/// behind you, which stops you and missed shots, and walls down both sides and across the far end,
/// which stop players (you and the actors) but not structures - shots may start outside the lane
/// and cut across it. Walls are on the floor's own physics layer, which players already collide
/// with; each live structure is told to ignore the side and end walls (<see cref="LetStructuresThrough"/>).
/// Built from primitives; only the walls keep colliders.
/// </summary>
internal sealed class LaneVisuals
{
    private const float LineThickness = 0.04f;
    private const float LineHeight = 0.02f;
    private const float BackstopGap = 0.9f;
    private const float BackstopHeight = 3f;
    /// <summary>
    /// Thick, growing outward from their inner faces: a dash moves a player far enough in one physics
    /// step to pass through a thin wall (0.3 m walls were dashed through in VR).
    /// </summary>
    private const float BackstopThickness = 2f;
    private const float FrameThickness = 0.06f;

    /// <summary>Side walls stand this far outside the lane's edge lines: room for a body on the line.</summary>
    private const float SideGap = 0.3f;

    /// <summary>Tall enough that knockback does not throw anyone over.</summary>
    private const float WallHeight = 8f;

    private const float WallThickness = 2f;

    /// <summary>The backstop's and far wall's inner faces stand this far beyond the lane's ends.</summary>
    private const float EndGap = 0.75f;
    private const float ReapplySeconds = 0.1f;

    private static readonly Color EdgeColour = new(1f, 0.45f, 0.1f);
    private static readonly Color EndColour = new(1f, 0.85f, 0.2f);
    private static readonly Color BackstopColour = new(0.15f, 0.15f, 0.18f);
    private static readonly Color ZoneColour = new(0.95f, 0.95f, 0.85f);

    /// <summary>Your zone's wall stands this far past its line - room to stand on the line.</summary>
    private const float ZoneGap = 0.3f;

    private readonly List<GameObject> _parts = [];
    private readonly List<Collider> _playerOnlyWalls = [];
    private readonly List<Collider> _zoneWalls = [];
    private readonly Dictionary<IntPtr, (bool Active, Collider[] Colliders)> _seen = [];
    private float _nextReapply;

    /// <param name="frame">Where the lane is.</param>
    /// <param name="lane">Its size.</param>
    /// <param name="showBackstop">Draw the backstop's frame.</param>
    /// <param name="walls">Build the walls - off for lane setup's preview, which you walk around.</param>
    /// <param name="zone">Your part of the lane - a wall across it and a ceiling over it - or null for none (recording).</param>
    /// <param name="colours">The colours to start in; with a wall colour the walls are seen, as panes on their inner faces.</param>
    /// <param name="panes">Build the wall panes even if <paramref name="colours"/> hides them - a later phase shows them.</param>
    public void Build(LaneFrame frame, Lane lane, bool showBackstop, bool walls = true, Zone? zone = null, LaneColours? colours = null, bool panes = false)
    {
        Destroy();
        colours ??= LaneColours.Default;
        foreach ((Role role, Color colour) in Targets(colours)) _now[role] = colour;
        Color edgeColour = _now[Role.Edge];
        Color endColour = _now[Role.End];

        var length = lane.Length * frame.DepthScale;
        var halfWidth = lane.Width / 2f;
        Vector3 forward = Describe.ToUnity(frame.Forward);
        var along = Quaternion.LookRotation(forward);

        Vector3 Point(float x, float z)
        {
            return Describe.ToUnity(frame.ToWorld(new System.Numerics.Vector3(x, LineHeight, z)));
        }

        // Edges run the length of the lane; ends cross it.
        Line(Point(-halfWidth, lane.Length / 2f), along, new Vector3(LineThickness, LineHeight, length), edgeColour, Role.Edge);
        Line(Point(halfWidth, lane.Length / 2f), along, new Vector3(LineThickness, LineHeight, length), edgeColour, Role.Edge);
        Line(Point(0f, 0f), along, new Vector3(lane.Width, LineHeight, LineThickness), endColour, Role.End);
        Line(Point(0f, lane.Length), along, new Vector3(lane.Width, LineHeight, LineThickness), endColour, Role.End);

        // The backstop is a solid wall to physics but only a frame to the eye - drawn solid it hid
        // the lane from anything behind you.
        Vector3 origin = Describe.ToUnity(frame.Origin);
        Vector3 wallCentre = origin - (forward * BackstopGap) + (Vector3.up * (BackstopHeight / 2f));
        var wallWidth = lane.Width + 2f;

        // The frame is drawn where the wall's face is; the solid part goes back from it.
        Vector3 solidCentre = origin - (forward * (EndGap + (BackstopThickness / 2f))) + (Vector3.up * (WallHeight / 2f));
        GameObject backstop = Part(solidCentre, along, new Vector3(wallWidth + (2f * WallThickness), WallHeight, BackstopThickness), BackstopColour, keepCollider: walls);
        backstop.name = "BossBattles_Backstop";
        backstop.layer = Arena.FloorLayer(origin);
        Renderer wall = backstop.GetComponent<Renderer>();
        wall?.enabled = false;

        // Like the other walls it stops players, not structures: shots you miss fly on past it.
        if (walls && backstop.GetComponent<Collider>() is { } backstopCollider) _playerOnlyWalls.Add(backstopCollider);

        if (showBackstop)
        {
            Color frameColour = _now[Role.Frame];
            Vector3 right = Describe.ToUnity(frame.Right);
            Line(wallCentre + (Vector3.up * (BackstopHeight / 2f)), along, new Vector3(wallWidth, FrameThickness, FrameThickness), frameColour, Role.Frame);
            Line(wallCentre - (right * (wallWidth / 2f)), along, new Vector3(FrameThickness, BackstopHeight, FrameThickness), frameColour, Role.Frame);
            Line(wallCentre + (right * (wallWidth / 2f)), along, new Vector3(FrameThickness, BackstopHeight, FrameThickness), frameColour, Role.Frame);
        }

        if (!walls)
        {
            Mod.Note("Lane.Preview", $"{length:F1} m x {lane.Width:F1} m", echo: false);
            return;
        }

        // Down both sides and across the far end, from the backstop to behind the actors.
        var runLength = length + (2f * (EndGap + BackstopThickness));
        var sideX = halfWidth + SideGap + (WallThickness / 2f);
        Vector3 farCentre = Point(0f, lane.Length + ((EndGap + (WallThickness / 2f)) / frame.DepthScale)) + (Vector3.up * (WallHeight / 2f));

        PlayerWall("BossBattles_WallLeft", Describe.ToUnity(frame.ToWorld(new System.Numerics.Vector3(-sideX, 0f, 0f))), along, forward, runLength, backstop.layer);
        PlayerWall("BossBattles_WallRight", Describe.ToUnity(frame.ToWorld(new System.Numerics.Vector3(sideX, 0f, 0f))), along, forward, runLength, backstop.layer);
        GameObject end = Part(farCentre, along, new Vector3(wallWidth + (2f * WallThickness), WallHeight, WallThickness), BackstopColour, keepCollider: true);
        end.name = "BossBattles_WallFar";
        end.layer = backstop.layer;
        Hide(end);
        _playerOnlyWalls.Add(end.GetComponent<Collider>());

        if (zone is { } yours) BuildZone(frame, lane, yours, along, forward, backstop.layer);
        if (panes || colours.ShowsWalls) BuildPanes(frame, lane, zone, along, _now[Role.Pane]);

        Mod.Note("Lane.Built", $"{length:F1} m x {lane.Width:F1} m (depth scale {frame.DepthScale:F2}), backstop on layer {LayerMask.LayerToName(backstop.layer)}", echo: false);
    }

    /// <summary>
    /// Every live structure ignores the side and end walls. Unity forgets an ignored pair when a
    /// collider is deactivated, and the game pools structures, so a structure seen inactive is told
    /// again when it comes back; others are re-told every <see cref="ReapplySeconds"/> anyway.
    /// </summary>
    public void LetStructuresThrough()
    {
        if (_playerOnlyWalls.Count == 0 || Time.time < _nextReapply) return;
        _nextReapply = Time.time + ReapplySeconds;

        CombatManager? combat = CombatManager.instance;
        if (combat == null) return;

        foreach (Structure structure in combat.Structures)
        {
            if (structure == null) continue;

            var active = structure.gameObject.activeInHierarchy;
            if (!_seen.TryGetValue(structure.Pointer, out (bool Active, Collider[] Colliders) known) || known.Colliders.Length == 0)
                known = (false, structure.GetComponentsInChildren<Collider>(true).ToArray());

            if (active)
            {
                foreach (Collider collider in known.Colliders)
                {
                    if (collider == null) continue;
                    foreach (Collider wall in _playerOnlyWalls)
                    {
                        if (wall != null) Physics.IgnoreCollision(collider, wall, true);
                    }
                }
            }

            _seen[structure.Pointer] = (active, known.Colliders);
        }
    }

    /// <summary>
    /// Your zone: a line across the floor where it ends, an invisible wall just past it and a
    /// ceiling over it - for you only: structures pass (<see cref="LetStructuresThrough"/>), and
    /// actors are let through by <see cref="LetActorsThrough"/>.
    /// </summary>
    private void BuildZone(LaneFrame frame, Lane lane, Zone zone, Quaternion along, Vector3 forward, int layer)
    {
        if (zone.Length < lane.Length - 0.01f)
        {
            Vector3 line = Describe.ToUnity(frame.ToWorld(new System.Numerics.Vector3(0f, LineHeight, zone.Length)));
            Line(line, along, new Vector3(lane.Width, LineHeight, LineThickness), _now[Role.Zone], Role.Zone);

            Vector3 wallFoot = Describe.ToUnity(frame.ToWorld(new System.Numerics.Vector3(0f, 0f, zone.Length)));
            GameObject wall = Part(wallFoot + (forward * (ZoneGap + (WallThickness / 2f))) + (Vector3.up * (WallHeight / 2f)), along,
                new Vector3(lane.Width + (2f * (SideGap + WallThickness)), WallHeight, WallThickness), BackstopColour, keepCollider: true);
            ZoneWall(wall, "BossBattles_ZoneEnd", layer);
        }

        // The ceiling covers the zone from behind the backstop to its far wall.
        var depth = (zone.Length * frame.DepthScale) + EndGap + BackstopThickness + ZoneGap + WallThickness;
        Vector3 under = Describe.ToUnity(frame.ToWorld(new System.Numerics.Vector3(0f, zone.Height, 0f)));
        Vector3 centre = under + (forward * ((depth / 2f) - EndGap - BackstopThickness)) + (Vector3.up * (WallThickness / 2f));
        GameObject ceiling = Part(centre, along, new Vector3(lane.Width + (2f * (SideGap + WallThickness)), WallThickness, depth), BackstopColour, keepCollider: true);
        ZoneWall(ceiling, "BossBattles_ZoneCeiling", layer);
    }

    /// <summary>
    /// The walls made visible: a thin see-through pane on each one's inner face - down both sides,
    /// across both ends and over your zone (not across it - that one stands between you and the boss;
    /// the zone line on the floor marks it) - as high as your zone. Drawn only; the walls
    /// themselves stay unseen.
    /// </summary>
    private void BuildPanes(LaneFrame frame, Lane lane, Zone? zone, Quaternion along, Color colour)
    {
        var height = zone?.Height ?? Zone.DefaultHeight;
        var halfWidth = (lane.Width / 2f) + SideGap;
        var scale = frame.DepthScale;
        var near = -EndGap / scale;
        var far = lane.Length + (EndGap / scale);

        Vector3 At(float x, float y, float z)
        {
            return Describe.ToUnity(frame.ToWorld(new System.Numerics.Vector3(x, y, z)));
        }

        var run = (far - near) * scale;
        Pane(At(-halfWidth, height / 2f, (near + far) / 2f), along, new Vector3(PaneThickness, height, run), colour);
        Pane(At(halfWidth, height / 2f, (near + far) / 2f), along, new Vector3(PaneThickness, height, run), colour);
        Pane(At(0f, height / 2f, near), along, new Vector3(halfWidth * 2f, height, PaneThickness), colour);
        Pane(At(0f, height / 2f, far), along, new Vector3(halfWidth * 2f, height, PaneThickness), colour);

        if (zone == null) return;

        // No pane across the lane at your zone's end: it stood between you and the boss, tinting
        // everything you fight (the zone line on the floor marks it). The ceiling pane runs the whole
        // lane, over the boss too - stopping at the zone's end left the far half untinted.
        Pane(At(0f, height, (near + far) / 2f), along, new Vector3(halfWidth * 2f, PaneThickness, run), colour);
    }

    private const float PaneThickness = 0.01f;

    private void Pane(Vector3 centre, Quaternion rotation, Vector3 size, Color colour)
    {
        GameObject pane = Part(centre, rotation, size, colour, keepCollider: false, glass: true);
        pane.name = "BossBattles_Pane";
        Remember(pane, Role.Pane);
    }

    // ---- Colours that change ---------------------------------------------------------------------

    /// <summary>The parts a chart colours, so a phase can recolour them.</summary>
    private enum Role
    {
        Edge,
        End,
        Zone,
        Frame,
        Pane,
    }

    private readonly List<(Role Role, Renderer Renderer, Material Material)> _painted = [];
    private readonly Dictionary<Role, Color> _now = [];
    private Dictionary<Role, Color> _from = [];
    private Dictionary<Role, Color> _to = [];
    private float _fadeStart;
    private float _fadeSeconds;

    /// <summary>The edge lines' colour right now - the self-test reads it.</summary>
    public Color? EdgeColourNow => _now.TryGetValue(Role.Edge, out Color c) ? c : null;

    /// <summary>Fades the lane to <paramref name="colours"/> over <paramref name="seconds"/> (0: at once).</summary>
    public void Recolour(LaneColours colours, float seconds)
    {
        if (_painted.Count == 0) return;

        _from = new Dictionary<Role, Color>(_now);
        _to = Targets(colours);
        _fadeStart = Time.time;
        _fadeSeconds = seconds;
        Tick();
    }

    /// <summary>Each frame: moves a fade on.</summary>
    public void Tick()
    {
        if (_to.Count == 0) return;

        var k = _fadeSeconds <= 0f ? 1f : Mathf.Clamp01((Time.time - _fadeStart) / _fadeSeconds);
        foreach ((Role role, Color target) in _to)
        {
            Color from = _from.TryGetValue(role, out Color f) ? f : target;
            Color to = target;

            // A pane fading in or out keeps its hue and only changes opacity.
            if (role == Role.Pane && from.a <= 0f) from = new Color(to.r, to.g, to.b, 0f);
            if (role == Role.Pane && to.a <= 0f) to = new Color(from.r, from.g, from.b, 0f);
            _now[role] = Color.Lerp(from, to, k);
        }

        foreach ((Role role, Renderer renderer, Material material) in _painted)
        {
            if (renderer == null || material == null) continue;
            Color colour = _now[role];
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
            if (material.HasProperty("_Color")) material.SetColor("_Color", colour);
            renderer.enabled = role != Role.Pane || colour.a > 0.004f;
        }

        if (k >= 1f) _to = [];
    }

    private static Dictionary<Role, Color> Targets(LaneColours colours)
    {
        Color edge = Pick(colours.Edges, EdgeColour);
        return new Dictionary<Role, Color>
        {
            [Role.Edge] = edge,
            [Role.End] = Pick(colours.Ends, EndColour),
            [Role.Zone] = Pick(colours.Zone, ZoneColour),
            [Role.Frame] = Pick(colours.Backstop, edge),
            [Role.Pane] = colours.Walls is { } walls ? ToColour(walls) : new Color(0f, 0f, 0f, 0f),
        };
    }

    private void Remember(GameObject part, Role role)
    {
        Renderer renderer = part.GetComponent<Renderer>();
        if (renderer == null) return;

        _painted.Add((role, renderer, renderer.material));
        if (role == Role.Pane && _now[Role.Pane].a <= 0.004f) renderer.enabled = false;
    }

    private static Color Pick(Rgba? chart, Color fallback) => chart is { } c ? ToColour(c) : fallback;

    private static Color ToColour(Rgba c) => new(c.R, c.G, c.B, c.A);

    private void ZoneWall(GameObject part, string name, int layer)
    {
        part.name = name;
        part.layer = layer;
        Hide(part);
        Collider collider = part.GetComponent<Collider>();
        _playerOnlyWalls.Add(collider);
        _zoneWalls.Add(collider);
    }

    /// <summary>Actors walk through your zone's wall and ceiling - a recorded actor may come up to you.</summary>
    public void LetActorsThrough(IEnumerable<Collider> actorColliders)
    {
        foreach (Collider actor in actorColliders)
        {
            if (actor == null) continue;
            foreach (Collider wall in _zoneWalls)
            {
                if (wall != null) Physics.IgnoreCollision(actor, wall, true);
            }
        }
    }

    public void Destroy()
    {
        foreach (GameObject part in _parts)
        {
            if (part != null) UnityEngine.Object.Destroy(part);
        }

        _parts.Clear();
        _painted.Clear();
        _to = [];
        _playerOnlyWalls.Clear();
        _zoneWalls.Clear();
        _seen.Clear();
    }

    /// <summary>For a scene change: the parts went with the scene.</summary>
    public void Forget()
    {
        _parts.Clear();
        _painted.Clear();
        _to = [];
        _playerOnlyWalls.Clear();
        _zoneWalls.Clear();
        _seen.Clear();
    }

    private void PlayerWall(string name, Vector3 nearFoot, Quaternion along, Vector3 forward, float runLength, int layer)
    {
        // From behind the backstop to beyond the far wall, so the corners leave no gap.
        Vector3 centre = nearFoot + (forward * ((runLength / 2f) - EndGap - BackstopThickness)) + (Vector3.up * (WallHeight / 2f));
        GameObject wall = Part(centre, along, new Vector3(WallThickness, WallHeight, runLength), BackstopColour, keepCollider: true);
        wall.name = name;
        wall.layer = layer;
        Hide(wall);
        _playerOnlyWalls.Add(wall.GetComponent<Collider>());
    }

    private static void Hide(GameObject part)
    {
        Renderer renderer = part.GetComponent<Renderer>();
        renderer?.enabled = false;
    }

    private void Line(Vector3 centre, Quaternion rotation, Vector3 size, Color colour, Role role) =>
        Remember(Part(centre, rotation, size, colour, keepCollider: false), role);

    private GameObject Part(Vector3 centre, Quaternion rotation, Vector3 size, Color colour, bool keepCollider, bool glass = false)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = "BossBattles_Lane";
        part.transform.SetPositionAndRotation(centre, rotation);
        part.transform.localScale = size;

        if (!keepCollider)
        {
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.Destroy(collider);
        }

        Renderer renderer = part.GetComponent<Renderer>();
        Material? material = glass ? Paint.Glass(colour) : Paint.Colour(colour);
        if (renderer != null && material != null) renderer.material = material;

        _parts.Add(part);
        return part;
    }
}
