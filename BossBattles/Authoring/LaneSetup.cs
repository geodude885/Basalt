using BossBattles.Arena;
using BossBattles.Battle;
using BossBattles.Services.Charts;
using UnityEngine;

namespace BossBattles.Authoring;

// A preview lane you walk around, resized and moved from the panel, then written into the chart.
internal class LaneSetup
{
    private readonly Arena.Arena _preview = new();
    private List<ActorSpec> _actors = [];
    private (Vector3 Feet, Vector3 Facing)? _laneStartedFrom;
    private Anchor? _previewAnchor;
    private bool _previewOutOfDate;

    public ArenaSpec ArenaSpec { get; private set; } = ArenaSpec.Here;

    public Lane Lane { get; private set; }

    public ActorSpec? FirstActor => _actors.FirstOrDefault();

    public void BeginWith(Chart chart)
    {
        ArenaSpec = chart.Arena;
        Lane = chart.Lane;
        _actors = chart.Actors.ToList();
        _laneStartedFrom = null;
        _previewOutOfDate = true;

        if (!ArenaSpec.Map.IsLoaded()) ArenaSpec.Map.Load();
    }

    public void ResizeLane(float lengthChange, float widthChange)
    {
        (Lane, var actors) = ChartSetup.Resize(Lane, _actors, Lane.Length + lengthChange, Lane.Width + widthChange);
        _actors = actors.ToList();
        _previewOutOfDate = true;
    }

    public void StartLaneWhereYouStand()
    {
        if (BattlePlayer.Rig is not { } rig) return;

        var map = Maps.Current;
        _laneStartedFrom = (rig.Feet, rig.Facing);
        if (map == ArenaKind.Here)
        {
            ArenaSpec = ArenaSpec.Here;
        }
        else
        {
            var fromArenaOrigin = rig.Feet - (map.GetArenaOrigin() ?? Vector3.zero);
            ArenaSpec = new ArenaSpec(map, MathF.Round(fromArenaOrigin.x, 2), MathF.Round(fromArenaOrigin.z, 2), MathF.Round(rig.Facing.Bearing()));
        }

        _previewOutOfDate = true;
    }

    // The lane grows to reach you if you stand past its far end.
    public void MoveFirstActorWhereYouStand()
    {
        if (BattlePlayer.Rig is not { } rig || _previewAnchor is not { } anchor || _actors.Count == 0) return;

        var inLane = anchor.ToLocal(rig.Feet.ToNumerics());
        var along = Math.Max(0.5f, inLane.Z);
        if (along > Lane.Length) Lane = new Lane(Math.Min(ChartSetup.MaxLength, MathF.Ceiling(along)), Lane.Width);

        var across = Math.Clamp(inLane.X, -Lane.Width / 2f, Lane.Width / 2f);
        _actors[0] = _actors[0] with { At = new Spot(MathF.Round(across, 2), MathF.Round(Math.Min(along, Lane.Length), 2)) };
        _previewOutOfDate = true;
    }

    public void SwitchToNextMap()
    {
        var next = (ArenaKind)(((int)ArenaSpec.Map + 1) % Enum.GetValues<ArenaKind>().Length);
        ArenaSpec = new ArenaSpec(next);
        _laneStartedFrom = null;
        _previewOutOfDate = true;
        if (!next.IsLoaded()) next.Load();
    }

    // Every distance along the lane is scaled with it, so nothing is left past the far end.
    public string WriteIntoChartText(string chartText, Chart chartBefore)
    {
        var lengthScale = chartBefore.Lane.Length > 0 ? Lane.Length / chartBefore.Lane.Length : 1f;
        return ChartSetup.ScaleDepth(ChartSetup.Write(chartText, ArenaSpec, Lane, _actors), lengthScale, Lane.Length);
    }

    public string DescribeLane()
    {
        var where = ArenaSpec.Map == ArenaKind.Here ? "here"
            : ArenaSpec.HasSpot ? $"{ArenaSpec.Map} at {ArenaSpec.X:0.#}, {ArenaSpec.Z:0.#} facing {ArenaSpec.Facing:0}"
            : ArenaSpec.Map.ToString();
        return $"{where}, {Lane.Length:0.#} x {Lane.Width:0.#} m";
    }

    public void RebuildPreviewIfOutOfDate()
    {
        if (!_previewOutOfDate || BattlePlayer.Rig is not { } rig) return;
        if (ArenaSpec.Map != ArenaKind.Here && !ArenaSpec.Map.IsLoaded()) return;

        _laneStartedFrom ??= (rig.Feet, rig.Facing);
        if (ArenaSpec.CreateLaneAnchor(ArenaSpec.Map, _laneStartedFrom.Value.Feet, _laneStartedFrom.Value.Facing) is not { } anchor) return;

        _previewAnchor = anchor;
        _previewOutOfDate = false;
        _preview.Build(anchor, Lane, showBackstop: true, walls: false);
        foreach (var actor in _actors) _preview.Visuals.ActorMarker(anchor.ToWorld(new System.Numerics.Vector3(actor.At.X, 0f, actor.At.Z)).ToUnity());
    }

    public void RemovePreview() => _preview.Destroy();

    public void ForgetPreviewAfterSceneChange()
    {
        _preview.Forget();
        _previewOutOfDate = true;
    }
}
