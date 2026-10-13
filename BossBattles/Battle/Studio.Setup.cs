using BossBattles.Services.Charts;
using UnityEngine;

namespace BossBattles.Battle;

/// <summary>Lane setup: a preview lane you walk around, resized and moved from the panel, then written into the chart.</summary>
internal sealed partial class Studio
{
    private ArenaSpec _setupArena = ArenaSpec.Here;
    private Lane _setupLane;
    private List<ActorSpec> _setupActors = [];
    private (Vector3 Floor, Vector3 Facing)? _setupFrom;
    private LaneFrame? _setupFrame;
    private bool _setupDirty;

    public (ArenaSpec Arena, Lane Lane, ActorSpec? Actor) SetupValues => (_setupArena, _setupLane, _setupActors.FirstOrDefault());

    /// <summary>Starts setting up <paramref name="entry"/>'s lane, in its own arena.</summary>
    public void BeginSetup(ChartEntry entry)
    {
        if (!Begin(entry, null)) return;

        Mod.Battle.Stop("lane setup");

        _setupArena = Chart!.Arena;
        _setupLane = Chart.Lane;
        _setupActors = Chart.Actors.ToList();
        _setupFrom = null;
        _setupDirty = true;
        State = StudioState.Setup;

        if (!Arena.InScene(_setupArena.Map)) Arena.Load(_setupArena.Map);
    }

    public void Resize(float lengthChange, float widthChange)
    {
        (_setupLane, IReadOnlyList<ActorSpec> actors) = ChartSetup.Resize(_setupLane, _setupActors, _setupLane.Length + lengthChange, _setupLane.Width + widthChange);
        _setupActors = actors.ToList();
        _setupDirty = true;
    }

    /// <summary>Your spot, facing the way you face, becomes the lane's start - on whichever map you are.</summary>
    public void LaneHere()
    {
        if (You() is not { } rig) return;

        ArenaKind map = Arena.CurrentMap();
        _setupFrom = (rig.Feet, rig.Facing);
        if (map == ArenaKind.Here)
        {
            _setupArena = ArenaSpec.Here;
        }
        else
        {
            Vector3 at = rig.Feet - (Arena.Anchor(map) ?? Vector3.zero);
            _setupArena = new ArenaSpec(map, MathF.Round(at.x, 2), MathF.Round(at.z, 2), MathF.Round(VectorHelpers.Bearing(rig.Facing)));
        }

        _setupDirty = true;
    }

    /// <summary>The actor's spot becomes where you stand; the lane grows to reach it if needed.</summary>
    public void ActorHere()
    {
        if (You() is not { } rig || _setupFrame is not { } frame || _setupActors.Count == 0) return;

        System.Numerics.Vector3 at = frame.ToLane(StringHelpers.ToNumerics(rig.Feet));
        var z = Math.Max(0.5f, at.Z);
        if (z > _setupLane.Length) _setupLane = new Lane(Math.Min(ChartSetup.MaxLength, MathF.Ceiling(z)), _setupLane.Width);

        var x = Math.Clamp(at.X, -_setupLane.Width / 2f, _setupLane.Width / 2f);
        _setupActors[0] = _setupActors[0] with { At = new Spot(MathF.Round(x, 2), MathF.Round(Math.Min(z, _setupLane.Length), 2)) };
        _setupDirty = true;
    }

    /// <summary>The next map, loading it when it is a different scene.</summary>
    public void NextMap()
    {
        var next = (ArenaKind)(((int)_setupArena.Map + 1) % Enum.GetValues<ArenaKind>().Length);
        _setupArena = new ArenaSpec(next);
        _setupFrom = null;
        _setupDirty = true;
        if (!Arena.InScene(next)) Arena.Load(next);
    }

    /// <summary>Writes the lane setup into the chart; returns what happened.</summary>
    public string SaveSetup()
    {
        if (Entry == null || State != StudioState.Setup) return "no lane setup to save";

        // Every distance along the lane is scaled with it, so nothing is left past the far end.
        var text = File.ReadAllText(Entry.File);
        var factor = Chart!.Lane.Length > 0 ? _setupLane.Length / Chart.Lane.Length : 1f;
        var written = ChartSetup.ScaleDepth(ChartSetup.Write(text, _setupArena, _setupLane, _setupActors), factor, _setupLane.Length);
        Save(text, written);
        var where = _setupArena.Map == ArenaKind.Here ? "here"
            : _setupArena.HasSpot ? $"{_setupArena.Map} at {_setupArena.X:0.#}, {_setupArena.Z:0.#} facing {_setupArena.Facing:0}"
            : _setupArena.Map.ToString();
        var what = $"saved {Entry.Name}'s lane: {where}, {_setupLane.Length:0.#} x {_setupLane.Width:0.#} m";
        EndSetup();
        Logger.Debug($"[Studio.SetupSaved] {what}");
        return what;
    }

    public void CancelSetup() => EndSetup();

    private void EndSetup()
    {
        _preview.Destroy();
        foreach (GameObject marker in _markers)
        {
            if (marker != null) UnityEngine.Object.Destroy(marker);
        }

        _markers.Clear();
        Off();
    }

    private void BuildPreview()
    {
        if (You() is not { } rig) return;
        if (_setupArena.Map != ArenaKind.Here && !Arena.InScene(_setupArena.Map)) return;

        _setupFrom ??= (rig.Feet, rig.Facing);
        LaneFrame? frame = Arena.Frame(_setupArena.Map, _setupArena, _setupLane, _setupFrom.Value.Floor, _setupFrom.Value.Facing);
        if (frame == null) return;

        _setupFrame = frame;
        _setupDirty = false;
        _preview.Build(frame.Value, _setupLane, showBackstop: true, walls: false);

        foreach (GameObject marker in _markers)
        {
            if (marker != null) UnityEngine.Object.Destroy(marker);
        }

        _markers.Clear();
        _markers.AddRange(_setupActors.Select(actor => Marker(StringHelpers.ToUnity(frame.Value.ToWorld(new System.Numerics.Vector3(actor.At.X, 0f, actor.At.Z))))));
    }

    /// <summary>A post where an actor will stand.</summary>
    private static GameObject Marker(Vector3 foot)
    {
        var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        post.name = "BossBattles_ActorMarker";
        Collider collider = post.GetComponent<Collider>();
        if (collider != null) UnityEngine.Object.Destroy(collider);
        post.transform.position = foot + (Vector3.up * 0.9f);
        post.transform.localScale = new Vector3(0.35f, 0.9f, 0.35f);

        Material? material = Paint.Colour(new Color(0.25f, 0.5f, 0.95f));
        Renderer renderer = post.GetComponent<Renderer>();
        if (renderer != null && material != null) renderer.material = material;
        return post;
    }
}
