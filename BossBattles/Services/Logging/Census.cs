namespace BossBattles.Services.Logging;

/// <summary>
/// Named counters, so "the clone never got hit" becomes "OnTriggerEnter=14, RegisterPlayerHitEvent=0".
/// </summary>
public sealed class Census
{
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

    public int this[string name] => _counts.TryGetValue(name, out var count) ? count : 0;

    public void Count(string name)
    {
        _counts.TryGetValue(name, out var count);
        _counts[name] = count + 1;
    }

    /// <summary>Highest count first, ties by name, so two reports of the same run read the same.</summary>
    public string Report() =>
        _counts.Count == 0
            ? "(nothing counted)"
            : string.Join(", ", _counts
                .OrderByDescending(p => p.Value)
                .ThenBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => $"{p.Key}={p.Value}"));

    public void Clear() => _counts.Clear();
}
