namespace BossBattles.Services.Charts;

/// <summary>
/// Plays a chart against a clock: <see cref="Advance"/> returns every command due since the last
/// call. At the same moment, phases come first, then placements, then the rest in chart order.
/// </summary>
public sealed class ChartRunner
{
    private const int RankPhase = 0;
    private const int RankPlace = 1;
    private const int RankSpawn = 2;
    private const int RankLaunch = 3;
    private const int RankMove = 4;
    private const int RankEnd = 5;

    private readonly List<ChartCommand> _schedule;
    private int _next;
    private double _now = double.NegativeInfinity;

    /// <param name="chart">The chart.</param>
    /// <param name="startAt">
    /// Chart time to start from. What is in force then is brought forward (the phase, actors' spots,
    /// a clip part-way through, a projectile telegraphing); what had finished is dropped.
    /// </param>
    public ChartRunner(Chart chart, double startAt = 0)
    {
        Chart = chart;
        StartAt = startAt;
        _schedule = startAt > 0 ? From(BuildSchedule(chart), startAt) : BuildSchedule(chart);
    }

    public double StartAt { get; }

    public Chart Chart { get; }

    public IReadOnlyList<ChartCommand> Schedule => _schedule;

    public bool Finished => _next >= _schedule.Count;

    /// <summary>Every command with a time at or before <paramref name="now"/> not yet returned.</summary>
    /// <exception cref="ArgumentException">If the clock goes backwards.</exception>
    public IReadOnlyList<ChartCommand> Advance(double now)
    {
        if (now < _now) throw new ArgumentException($"the clock went backwards: {now} after {_now}", nameof(now));
        _now = now;

        var first = _next;
        while (_next < _schedule.Count && _schedule[_next].Time <= now) _next++;
        return _next == first ? Array.Empty<ChartCommand>() : _schedule.GetRange(first, _next - first);
    }

    private static List<ChartCommand> From(List<ChartCommand> schedule, double startAt)
    {
        var started = new List<ChartCommand>();
        var launchedLate = schedule.OfType<LaunchProjectile>().Where(l => l.Time >= startAt).Select(l => l.Id).ToHashSet();

        EnterPhase? phase = schedule.OfType<EnterPhase>().LastOrDefault(p => p.Time <= startAt);
        if (phase != null) started.Add(phase with { Time = startAt });

        foreach (PlaceActor place in schedule.OfType<PlaceActor>().Where(p => p.Time <= startAt).GroupBy(p => p.ActorId).Select(g => g.Last()))
            started.Add(place with { Time = startAt });

        foreach (PlayClip clip in schedule.OfType<PlayClip>().Where(t => t.Time <= startAt && (t.To == null || t.Time + (t.To - t.From) > startAt)))
            started.Add(clip with { Time = startAt, From = clip.From + (startAt - clip.Time) });

        foreach (SpawnProjectile spawn in schedule.OfType<SpawnProjectile>().Where(s => s.Time < startAt && launchedLate.Contains(s.Id)))
            started.Add(spawn with { Time = startAt });

        // Brought-forward state first, in the usual order, then everything still to come.
        started.AddRange(schedule.Where(c => c.Time > startAt || (c.Time == startAt && c is not (EnterPhase or PlaceActor or PlayClip))));
        return started;
    }

    private static List<ChartCommand> BuildSchedule(Chart chart)
    {
        var entries = new List<(ChartCommand Command, int Rank, int Order)>();

        entries.AddRange(chart.Actors.Select((actor, i) => ((ChartCommand)new PlaceActor(0, actor.Id, actor.At), RankPlace, -chart.Actors.Count + i)));

        foreach ((Phase phase, var i) in chart.Phases.Select((p, i) => (p, i)))
        {
            entries.Add((new EnterPhase(phase.Start, phase), RankPhase, i));
            if (phase.At is Spot spot) entries.Add((new PlaceActor(phase.Start, phase.ActorId ?? chart.DefaultActor.Id, spot), RankPlace, i));
        }

        var projectileId = 0;
        foreach (ChartEvent chartEvent in chart.Events)
        {
            switch (chartEvent)
            {
                case ProjectileEvent projectile:
                    projectileId++;
                    entries.Add((new SpawnProjectile(projectile.SpawnTime, projectileId, projectile), RankSpawn, projectile.Order));
                    entries.Add((new LaunchProjectile(projectile.Time, projectileId, projectile), RankLaunch, projectile.Order));
                    break;
                case MoveEvent move:
                    entries.Add((new RunMove(move.Time, move.ActorId, move.Stack), RankMove, move.Order));
                    break;
                case PlaceEvent place:
                    entries.Add((new PlaceActor(place.Time, place.ActorId, place.At), RankPlace, place.Order));
                    break;
            }
        }

        foreach (Track track in chart.Tracks)
        {
            // Each clip plays until its own end or the next clip, whichever is first.
            IEnumerable<double?> nextStarts = track.Clips.Skip(1).Select(t => (double?)t.Time).Append(null);
            foreach ((TrackClip clip, var next) in track.Clips.Zip(nextStarts))
            {
                var to = clip.To;
                if (next != null) to = Math.Min(to ?? double.MaxValue, clip.From + (next.Value - clip.Time));
                entries.Add((new PlayClip(clip.Time, track.ActorId, clip.Clip, clip.From, to) { Mime = clip.Mime }, RankMove, -1));
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
