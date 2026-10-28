using Basalt.Services.Charts;
using Basalt.Services.Clips;
using Basalt.Services.Scoring;

namespace Basalt.Battle;

internal class BattleRun(BattleRequest request, ClipLibrary? chartClips)
{
    public BattleRequest Request => request;
    public BattleState State { get; set; } = BattleState.LoadingArena;
    public Anchor Frame { get; set; }

    // The lane's frame once it is laid out; null while the arena loads.
    public Anchor? Lane => State == BattleState.LoadingArena ? null : Frame;
    public ChartRunner? Runner { get; set; }
    public ScoreKeeper Score { get; } = new(request.Chart.Name);
    public Phase Phase { get; set; } = request.Chart.Phases[0];
    public float SpawnDeadline { get; set; }
    public float CountdownEnds { get; set; }
    public float MusicDeadline { get; set; }
    public float StartedAt { get; set; }

    public Clip? FindClip(string name) =>
        request.PendingClip != null && string.Equals(request.PendingClip.Name, name, StringComparison.OrdinalIgnoreCase)
            ? request.PendingClip
            : chartClips?.Find(name);
}
