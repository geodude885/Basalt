namespace BossBattles.Services.Charts;

/// <summary>A tempo from a beat on: <see cref="Bpm"/> beats per minute from <see cref="Beat"/>.</summary>
public readonly record struct TempoChange(double Beat, double Bpm);

/// <summary>
/// A chart's tempo - one bpm, or a list of changes - turning beats into seconds and back. Each
/// change holds from its beat to the next, so beat 70 in <c>120 bpm from 0, 140 from 64</c> is
/// 64 beats at 120 then 6 at 140.
/// </summary>
public sealed class TempoMap
{
    private readonly TempoChange[] _changes;
    private readonly double[] _startSeconds;

    /// <param name="changes">At least one, the first at beat 0, beats increasing, every bpm over 0 - the loader checks.</param>
    public TempoMap(IReadOnlyList<TempoChange> changes)
    {
        if (changes.Count == 0 || changes[0].Beat != 0) throw new ArgumentException("a tempo map starts at beat 0", nameof(changes));

        _changes = changes.ToArray();
        _startSeconds = new double[_changes.Length];
        for (var i = 1; i < _changes.Length; i++)
            _startSeconds[i] = _startSeconds[i - 1] + ((_changes[i].Beat - _changes[i - 1].Beat) * 60.0 / _changes[i - 1].Bpm);
    }

    public static TempoMap Constant(double bpm) => new(new[] { new TempoChange(0, bpm) });

    public IReadOnlyList<TempoChange> Changes => _changes;

    /// <summary>Seconds from the start to <paramref name="beat"/>.</summary>
    public double Seconds(double beat)
    {
        var i = Array.FindLastIndex(_changes, c => c.Beat <= beat);
        if (i < 0) i = 0;
        return _startSeconds[i] + ((beat - _changes[i].Beat) * 60.0 / _changes[i].Bpm);
    }

    /// <summary>The beat at <paramref name="seconds"/> from the start.</summary>
    public double Beat(double seconds)
    {
        var i = Array.FindLastIndex(_startSeconds, s => s <= seconds);
        if (i < 0) i = 0;
        return _changes[i].Beat + ((seconds - _startSeconds[i]) * _changes[i].Bpm / 60.0);
    }

    /// <summary>The tempo in force at <paramref name="seconds"/>.</summary>
    public double BpmAt(double seconds)
    {
        var i = Array.FindLastIndex(_startSeconds, s => s <= seconds);
        return _changes[Math.Max(0, i)].Bpm;
    }
}
