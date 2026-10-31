using Basalt.Services.Charts;
using Basalt.Services.Charts.Model;
using Basalt.Services.Clips;

namespace Basalt.Battle;

internal record BattleRequest(Chart Chart)
{
    public string? AssetFolder { get; init; }
    public string? ScoreKey { get; init; }
    public BattleMode Mode { get; init; } = BattleMode.Play;
    public string? RecordActor { get; init; }
    public Anchor? Lane { get; init; }
    public Clip? PendingClip { get; init; }
    public bool Mime { get; init; }
    public ActorSpec Performer => Chart.Actors.FirstOrDefault(a => a.Id == RecordActor) ?? Chart.DefaultActor;
}
