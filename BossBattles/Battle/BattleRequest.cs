using BossBattles.Services.Charts;
using BossBattles.Services.Clips;

namespace BossBattles.Battle;

// What a battle is asked to be: a chart to play, and for the studio how it is recorded or reviewed.
internal record BattleRequest(Chart Chart)
{
    // An arena to play in instead of the chart's own.
    public ArenaKind? ArenaOverride { get; init; }

    // Where the chart's song and clips/ are.
    public string? AssetFolder { get; init; }

    // Files the result in the score book; null keeps no score.
    public string? ScoreKey { get; init; }

    public BattleMode Mode { get; init; } = BattleMode.Play;

    // Recording: the actor whose place you take; the chart's first when not given.
    public string? RecordActor { get; init; }

    // A lane already laid out (a clip's) instead of laying one now.
    public Anchor? Lane { get; init; }

    // A clip not yet saved, found by name before the chart's own clips.
    public Clip? PendingClip { get; init; }

    // Chart time to start from, for a section.
    public double StartAt { get; init => field = Math.Max(0, value); }

    // Recording: where in the lane to stand instead of the actor's spot.
    public System.Numerics.Vector2? RecordSpot { get; init; }

    // Recording a mime: you throw the chart's shots by hand.
    public bool Mime { get; init; }

    public ArenaKind Arena => ArenaOverride ?? Chart.Arena.Map;

    // An overridden arena keeps the chart's spot only on the chart's own map.
    public ArenaSpec ArenaSpec => Arena == Chart.Arena.Map ? Chart.Arena : new ArenaSpec(Arena);

    public bool Recording => Mode == BattleMode.Record;

    public bool Miming => Recording && Mime;

    public ActorSpec Performer => Chart.Actors.FirstOrDefault(a => a.Id == RecordActor) ?? Chart.DefaultActor;
}
