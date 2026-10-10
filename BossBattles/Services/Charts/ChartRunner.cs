namespace BossBattles.Services.Charts;

/// <summary>
/// Plays a chart against a clock: <see cref="Advance"/> returns every command due since the last
/// call, in order. Commands due at the same moment come phase first, then actor placement, then
/// spawns, launches and moves, each in chart order - so a phase's actor spot is in place before
/// anything that phase throws.
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
    /// Chart time to start from - re-recording a section, practising one. What is in force then is
    /// brought forward to it (the phase, actors' spots, a take part-way through - from the right
    /// point - and a projectile already telegraphing); what had finished is dropped.
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

        var due = new List<ChartCommand>();
        while (_next < _schedule.Count && _schedule[_next].Time <= now)
        {
            due.Add(_schedule[_next]);
            _next++;
        }

        return due;
    }

    /// <summary>The phase in force at <paramref name="time"/>.</summary>
    public Phase PhaseAt(double time)
    {
        Phase current = Chart.Phases[0];
        foreach (Phase phase in Chart.Phases)
        {
            if (phase.Start > time) break;
            current = phase;
        }

        return current;
    }

    private static List<ChartCommand> From(List<ChartCommand> schedule, double startAt)
    {
        var started = new List<ChartCommand>();
        var launchedLate = schedule.OfType<LaunchProjectile>().Where(l => l.Time >= startAt).Select(l => l.Id).ToHashSet();

        EnterPhase? phase = schedule.OfType<EnterPhase>().LastOrDefault(p => p.Time <= startAt);
        if (phase != null) started.Add(phase with { Time = startAt });

        foreach (PlaceActor place in schedule.OfType<PlaceActor>().Where(p => p.Time <= startAt).GroupBy(p => p.ActorId).Select(g => g.Last()))
            started.Add(place with { Time = startAt });

        foreach (PlayTake take in schedule.OfType<PlayTake>().Where(t => t.Time <= startAt && (t.Until == null || t.Until > startAt)))
            started.Add(take with { Time = startAt, From = take.From + (startAt - take.Time) });

        foreach (SpawnProjectile spawn in schedule.OfType<SpawnProjectile>().Where(s => s.Time < startAt && launchedLate.Contains(s.Id)))
            started.Add(spawn with { Time = startAt });

        // Brought-forward state first, in the usual order, then everything still to come.
        started.AddRange(schedule.Where(c => c.Time > startAt || (c.Time == startAt && c is not (EnterPhase or PlaceActor or PlayTake))));
        return started;
    }

    private static List<ChartCommand> BuildSchedule(Chart chart)
    {
        var entries = new List<(ChartCommand Command, int Rank, int Order)>();

        for (var i = 0; i < chart.Actors.Count; i++)
            entries.Add((new PlaceActor(0, chart.Actors[i].Id, chart.Actors[i].At), RankPlace, -chart.Actors.Count + i));

        for (var i = 0; i < chart.Phases.Count; i++)
        {
            Phase phase = chart.Phases[i];
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
                case ClipEvent clip:
                    entries.Add((new PlayClip(clip.Time, clip.ActorId, clip.Clip), RankMove, clip.Order));
                    break;
                case PlaceEvent place:
                    entries.Add((new PlaceActor(place.Time, place.ActorId, place.At), RankPlace, place.Order));
                    break;
            }
        }

        foreach (Track track in chart.Tracks)
        {
            for (var i = 0; i < track.Takes.Count; i++)
            {
                TakeSegment take = track.Takes[i];
                double? until = take.To is { } to ? take.Time + (to - take.From) : null;
                if (i + 1 < track.Takes.Count) until = Math.Min(until ?? double.MaxValue, track.Takes[i + 1].Time);
                entries.Add((new PlayTake(take.Time, track.ActorId, take.Clip, take.From, until) { Mime = take.Mime }, RankMove, -1));
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
