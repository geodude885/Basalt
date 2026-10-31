namespace Basalt.Services.Charts.Model;

public readonly record struct TempoSection(double Beat, double Bpm);

public class TempoMap
{
    private readonly SectionTiming[] _sections;

    public TempoMap(IReadOnlyList<TempoSection> changes)
    {
        if (changes.Count == 0 || changes[0].Beat != 0) throw new ArgumentException("a tempo map starts at beat 0", nameof(changes));

        var sections = new List<SectionTiming> { new(0, changes[0].Bpm, 0) };
        foreach (var change in changes.Skip(1))
            sections.Add(new(change.Beat, change.Bpm, sections[^1].SecondsAt(change.Beat)));
        _sections = sections.ToArray();
    }

    public static TempoMap Constant(double bpm) => new([new TempoSection(0, bpm)]);

    public double BeatToSeconds(double beat) => Last(s => s.Beat <= beat).SecondsAt(beat);

    public double SecondsToBeat(double seconds) => SectionAt(seconds).BeatAt(seconds);

    public double BpmAt(double seconds) => SectionAt(seconds).Bpm;


    private SectionTiming SectionAt(double seconds)
        => Last(s => s.Seconds <= seconds);

    private SectionTiming Last(Func<SectionTiming, bool> match)
        => _sections.LastOrDefault(match, _sections[0]);

    private readonly record struct SectionTiming(double Beat, double Bpm, double Seconds)
    {
        public double SecondsAt(double beat) => Seconds + ((beat - Beat) * 60.0 / Bpm);

        public double BeatAt(double seconds) => Beat + ((seconds - Seconds) * Bpm / 60.0);
    }
}
