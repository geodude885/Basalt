using System.Text.Json;

namespace BossBattles.Services.Scoring;

/// <summary>Your best run of one chart.</summary>
public record BestScore(int Score, string Rank, bool Survived, DateTime When);

/// <summary>
/// Best scores per chart in a JSON file, keyed by <see cref="Charts.ChartEntry.ScoreKey"/>. An
/// unreadable file starts an empty book and is left alone until a new best overwrites it.
/// </summary>
public class ScoreBook
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _file;
    private Dictionary<string, BestScore> _best = new(StringComparer.Ordinal);

    public ScoreBook(string file)
    {
        _file = file;
    }

    /// <summary>Why the file could not be read, if it could not.</summary>
    public string? LoadError { get; private set; }

    public void Load()
    {
        LoadError = null;
        _best = new Dictionary<string, BestScore>(StringComparer.Ordinal);
        if (!File.Exists(_file)) return;

        try
        {
            var read = JsonSerializer.Deserialize<Dictionary<string, BestScore>>(File.ReadAllText(_file), Options);
            if (read != null) _best = new Dictionary<string, BestScore>(read, StringComparer.Ordinal);
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            LoadError = e.Message;
        }
    }

    public BestScore? Best(string key) => _best.TryGetValue(key, out var best) ? best : null;

    /// <summary>Records a run; returns true (and saves) when it beats the best so far.</summary>
    public bool Record(string key, BattleResult result, DateTime when)
    {
        var best = Best(key);
        if (best != null && best.Score >= result.Score) return false;

        _best[key] = new BestScore(result.Score, result.Rank, result.Survived, when);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_file))!);
        File.WriteAllText(_file, JsonSerializer.Serialize(_best, Options));
        return true;
    }
}
