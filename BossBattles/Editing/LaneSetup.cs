using BossBattles.Arenas;
using BossBattles.Battle;
using BossBattles.Presentation;
using BossBattles.Services.Charts;
using UnityEngine;

namespace BossBattles.Editing;

// A preview lane you walk around, resized and moved from the panel, then written into the chart.
internal class LaneSetup(GameAssets assets)
{
    public ArenaSpec ArenaSpec { get; private set; } = ArenaSpec.Here;
    public Lane Lane { get; private set; }

    public Arena ArenaPreview { get; private set; } = new(assets);
    public List<ActorSpec> Actors = [];

    private (Vector3 Feet, Vector3 Facing)? _laneStartedFrom;
    private Anchor? _previewAnchor;
    private bool _previewOutOfDate;


    public void BeginWith(Chart chart)
    {
        ArenaSpec = chart.Arena;
        Lane = chart.Lane;
        Actors = chart.Actors.ToList();
        _laneStartedFrom = null;
        _previewOutOfDate = true;

        if (!ArenaSpec.Map.IsLoaded()) ArenaSpec.Map.Load();
    }

    public void ResizeLane(float lengthChange, float widthChange)
    {
        (Lane, var actors) = LaneSetupWriter.Resize(Lane, Actors, Lane.Length + lengthChange, Lane.Width + widthChange);
        Actors = actors.ToList();
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
        if (BattlePlayer.Rig is not { } rig || _previewAnchor is not { } anchor || Actors.Count == 0) return;

        var inLane = anchor.ToLocal(rig.Feet.ToNumerics());
        var along = Math.Max(0.5f, inLane.Z);
        if (along > Lane.Length) Lane = new Lane(Math.Min(LaneSetupWriter.MaxLength, MathF.Ceiling(along)), Lane.Width);

        var across = Math.Clamp(inLane.X, -Lane.Width / 2f, Lane.Width / 2f);
        Actors[0] = Actors[0] with { At = new Spot(MathF.Round(across, 2), MathF.Round(Math.Min(along, Lane.Length), 2)) };
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
        return LaneSetupWriter.ScaleDepth(LaneSetupWriter.Write(chartText, ArenaSpec, Lane, Actors), lengthScale, Lane.Length);
    }

    public void RebuildPreviewIfOutOfDate()
    {
        if (!_previewOutOfDate || BattlePlayer.Rig is not { } rig) return;
        if (ArenaSpec.Map != ArenaKind.Here && !ArenaSpec.Map.IsLoaded()) return;

        _laneStartedFrom ??= (rig.Feet, rig.Facing);
        if (ArenaSpec.CreateLaneAnchor(ArenaSpec.Map, _laneStartedFrom.Value.Feet, _laneStartedFrom.Value.Facing) is not { } anchor) return;

        _previewAnchor = anchor;
        _previewOutOfDate = false;
        ArenaPreview.Build(anchor, Lane, showBackstop: true, walls: false);
        foreach (var actor in Actors) ArenaPreview.Visuals.ActorMarker(anchor.ToWorld(new System.Numerics.Vector3(actor.At.X, 0f, actor.At.Z)).ToUnity());
    }

    public void RemovePreview() => ArenaPreview.Destroy();

    public void ForgetPreviewAfterSceneChange()
    {
        ArenaPreview.Forget();
        _previewOutOfDate = true;
    }
}
