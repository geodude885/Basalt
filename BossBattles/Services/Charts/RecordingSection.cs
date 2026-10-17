namespace BossBattles.Services.Charts;

public readonly record struct RecordingSection(double From, double To)
{
    private const double SnapSecondsWithoutTempo = 0.5;

    // How far past the chart's end a section may run, for a performance that carries on after it.
    private const double OverrunSeconds = 30;

    public static RecordingSection MiddleThirdOf(Chart chart)
    {
        var section = new RecordingSection(SnapToBeat(chart, chart.End / 3), SnapToBeat(chart, chart.End * 2 / 3));
        return section.To > section.From ? section : section with { To = section.From + section.BeatLength(chart) };
    }

    public RecordingSection MoveStartByBeats(Chart chart, int beats)
    {
        var beat = BeatLength(chart);
        return new RecordingSection(SnapToBeat(chart, Math.Clamp(From + (beats * beat), 0, To - beat)), SnapToBeat(chart, To));
    }

    public RecordingSection MoveEndByBeats(Chart chart, int beats)
    {
        var beat = BeatLength(chart);
        return new RecordingSection(SnapToBeat(chart, From), SnapToBeat(chart, Math.Clamp(To + (beats * beat), From + beat, chart.End + OverrunSeconds)));
    }

    public double BeatLength(Chart chart) => chart.Tempo is { } tempo ? 60.0 / tempo.BpmAt(From) : SnapSecondsWithoutTempo;

    public static double SnapToBeat(Chart chart, double seconds) => chart.Tempo is { } tempo
        ? Math.Round(tempo.Seconds(Math.Round(tempo.Beat(seconds))), 3)
        : Math.Round(Math.Round(seconds / SnapSecondsWithoutTempo) * SnapSecondsWithoutTempo, 3);
}
