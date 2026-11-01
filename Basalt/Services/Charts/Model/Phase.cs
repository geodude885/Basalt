namespace Basalt.Services.Charts.Model;

public record Phase(string Name, double Time, double ScoreMultiplier)
{
    public LaneColours Colours { get; init; } = LaneColours.Default;
    internal static List<Phase> AllFrom(ChartDto dto, TempoMap? tempo)
    {
        if (dto.Phases is not { Count: > 0 } phases) return [new("Battle", 0, 1)];

        return phases.Select((phase, i) => From(phase, i, tempo)).ToList();
    }

    private static Phase From(PhaseDto phase, int index, TempoMap? tempo)
    {
        return new Phase(
            string.IsNullOrWhiteSpace(phase.Name) ? $"Phase {index + 1}" : phase.Name,
            (phase.Beat is { } beats ? tempo?.BeatToSeconds(beats) : phase.T)!.Value,
            phase.ScoreMultiplier ?? 1)
        { Colours = LaneColours.From(phase.Colours) };
    }

}
