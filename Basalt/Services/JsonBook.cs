using System.Text.Json;

namespace Basalt.Services;

// Entries by name in a JSON file, saved whenever one is set. An unreadable file starts an empty book,
// and is left alone until the next save overwrites it.
public class JsonBook<T>(string file)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private Dictionary<string, T> _entries = new(StringComparer.Ordinal);

    public string? LoadError { get; private set; }

    public void Load()
    {
        LoadError = null;
        _entries = new Dictionary<string, T>(StringComparer.Ordinal);
        if (!File.Exists(file)) return;

        try
        {
            var read = JsonSerializer.Deserialize<Dictionary<string, T>>(File.ReadAllText(file), Options);
            if (read != null) _entries = new Dictionary<string, T>(read, StringComparer.Ordinal);
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            LoadError = e.Message;
        }
    }

    public T? Get(string name) => _entries.TryGetValue(name, out var entry) ? entry : default;

    public void Set(string name, T entry)
    {
        _entries[name] = entry;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        File.WriteAllText(file, JsonSerializer.Serialize(_entries, Options));
    }
}
