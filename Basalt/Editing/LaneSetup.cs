using Basalt.Arenas;
using Basalt.Battle;
using Basalt.BattlePresentation;
using Basalt.Services.Charts;
using Basalt.Services.Charts.Files;
using Basalt.Services.Charts.Model;

namespace Basalt.Editing;

internal class LaneSetup(GameAssets assets)
{
    public ArenaSpec ArenaSpecification { get; private set; } = ArenaSpec.Default;
    public Lane Lane { get; private set; }
    public Arena ArenaPreview { get; private set; } = new(assets);
    public List<ActorSpec> Actors = [];

    private Anchor? _previewAnchor;
    private bool _previewOutOfDate;


    public void SetupFrom(Chart chart)
    {
        ArenaSpecification = chart.Arena;
        Lane = chart.Lane;
        Actors = chart.Actors.ToList();
        _previewOutOfDate = true;

        if (!ArenaSpecification.Map.IsLoaded()) ArenaSpecification.Map.Load();
    }

    public void ResizeLane(float lengthChange, float widthChange)
    {
        (Lane, var actors) = Lane.Resize(Actors, Lane.Length + lengthChange, Lane.Width + widthChange);
        Actors = actors.ToList();
        _previewOutOfDate = true;
    }

    public void StartLaneWherePlayerStands()
    {
        if (BattlePlayer.Rig is not { } rig || Maps.Current is not { } map) return;

        var fromArenaOrigin = rig.Feet - map.GetArenaOrigin();
        ArenaSpecification = new ArenaSpec(map, MathF.Round(fromArenaOrigin.x, 2), MathF.Round(fromArenaOrigin.z, 2), MathF.Round(rig.Facing.Bearing()));
        _previewOutOfDate = true;
    }

    public void MoveFirstActorWherePlayerStands()
    {
        if (BattlePlayer.Rig is not { } rig || _previewAnchor is not { } anchor || Actors.Count == 0) return;

        var inLane = anchor.ToLocal(rig.Feet);
        var along = Math.Max(0.5f, inLane.z);
        if (along > Lane.Length) Lane = new Lane(Math.Min(Lane.MaxLength, MathF.Ceiling(along)), Lane.Width);

        var across = Math.Clamp(inLane.x, -Lane.Width / 2f, Lane.Width / 2f);
        Actors[0] = Actors[0] with { At = new Spot(MathF.Round(across, 2), MathF.Round(Math.Min(along, Lane.Length), 2)) };
        _previewOutOfDate = true;
    }

    public void SwitchToNextMap()
    {
        var next = (ArenaKind)(((int)ArenaSpecification.Map + 1) % Enum.GetValues<ArenaKind>().Length);
        ArenaSpecification = new ArenaSpec(next);
        _previewOutOfDate = true;
        if (!next.IsLoaded()) next.Load();
    }

    public string WriteIntoChartText(string chartText, Chart chartBefore)
    {
        // Scales all chart move z lengths
        var lengthScale = chartBefore.Lane.Length > 0 ? Lane.Length / chartBefore.Lane.Length : 1f;
        return ChartWriter.ScaleChartOnLaneLengthChange(ChartWriter.WriteLaneSetup(chartText, ArenaSpecification, Lane, Actors), lengthScale, Lane.Length);
    }

    public void RebuildPreviewIfOutOfDate()
    {
        if (!_previewOutOfDate || !ArenaSpecification.Map.IsLoaded()) return;
        if (ArenaSpecification.CreateLaneAnchor() is not { } anchor) return;

        _previewAnchor = anchor;
        _previewOutOfDate = false;
        ArenaPreview.Create(anchor, Lane, showBackstop: true, walls: false);
        foreach (var actor in Actors) ArenaPreview.Visuals.ActorMarker(anchor.ToWorld(actor.At.X, 0f, actor.At.Z));
    }

    public void RemovePreview() => ArenaPreview.Destroy();

    public void ForgetPreviewAfterSceneChange()
    {
        ArenaPreview.Forget();
        _previewOutOfDate = true;
    }
}
