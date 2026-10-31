using Basalt.Services.Charts.Model;

namespace Basalt.Services.Charts;

public class ChartRunner
{
    public Chart Chart { get; }
    public IReadOnlyList<ChartCommand> Schedule => _schedule;
    public bool Finished => _next >= _schedule.Count;

    private const int RankPhase = 0;
    private const int RankPlace = 1;
    private const int RankSpawn = 2;
    private const int RankLaunch = 3;
    private const int RankMove = 4;
    private const int RankEnd = 5;

    private readonly List<ChartCommand> _schedule;
    private int _next;
    private double _now = double.NegativeInfinity;

    public ChartRunner(Chart chart)
    {
        Chart = chart;
        _schedule = BuildSchedule(chart);
    }

    public IReadOnlyList<ChartCommand> Advance(double now)
    {
        if (now < _now) throw new ArgumentException($"the clock went backwards: {now} after {_now}", nameof(now));
        _now = now;

        var first = _next;
        while (_next < _schedule.Count && _schedule[_next].Time <= now) _next++;
        return _next == first ? Array.Empty<ChartCommand>() : _schedule.GetRange(first, _next - first);
    }

    private static List<ChartCommand> BuildSchedule(Chart chart)
    {
        var entries = new List<(ChartCommand Command, int Rank, int Order)>();

        entries.AddRange(chart.Actors.Select((actor, i) => ((ChartCommand)new PlaceActor(0, actor.Id, actor.At), RankPlace, -chart.Actors.Count + i)));

        foreach ((var phase, var i) in chart.Phases.Select((p, i) => (p, i)))
            entries.Add((new EnterPhase(phase.Time, phase), RankPhase, i));

        var projectileId = 0;
        foreach (var chartEvent in chart.Events)
        {
            switch (chartEvent)
            {
                case ProjectileEvent projectile:
                    projectileId++;
                    entries.Add((new SpawnProjectile(projectile.Time - projectile.Telegraph, projectileId, projectile), RankSpawn, projectile.Order));
                    entries.Add((new LaunchProjectile(projectile.Time, projectileId, projectile), RankLaunch, projectile.Order));
                    break;
                case MoveEvent move:
                    entries.Add((new RunMove(move.Time, move.ActorId, move.Move), RankMove, move.Order));
                    break;
                case PlaceEvent place:
                    entries.Add((new PlaceActor(place.Time, place.ActorId, place.At), RankPlace, place.Order));
                    break;
            }
        }

        foreach (var performance in chart.Performances)
        {
            var nextStarts = performance.Clips.Skip(1).Select(t => (double?)t.Time).Append(null);
            foreach ((var clip, var next) in performance.Clips.Zip(nextStarts))
            {
                var to = clip.End;
                if (next != null) to = Math.Min(to ?? double.MaxValue, clip.Start + (next.Value - clip.Time));
                entries.Add((new PlayClip(clip.Time, performance.ActorId, clip.Clip, clip.Start, to) { Mime = clip.Mime }, RankMove, -1));
            }
        }

        entries.Add((new EndChart(chart.End), RankEnd, int.MaxValue));

        return entries
            .OrderBy(e => e.Command.Time)
            .ThenBy(e => e.Rank)
            .ThenBy(e => e.Order)
            .Select(e => e.Command)
            .ToList();
    }
}
