using System.Text.Json;

namespace BossBattles.Services.Placement;

/// <summary>Where something stands: a world position and a yaw in degrees.</summary>
public record SavedSpot(float X, float Y, float Z, float Yaw);

/// <summary>Remembered spots by name, in a JSON file; an unreadable file starts empty.</summary>
public class SpotBook
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _file;
    private Dictionary<string, SavedSpot> _spots = new(StringComparer.Ordinal);

    public SpotBook(string file)
    {
        _file = file;
    }

    public void Load()
    {
        _spots = new Dictionary<string, SavedSpot>(StringComparer.Ordinal);
        if (!File.Exists(_file)) return;

        try
        {
            Dictionary<string, SavedSpot>? read = JsonSerializer.Deserialize<Dictionary<string, SavedSpot>>(File.ReadAllText(_file), Options);
            if (read != null) _spots = new Dictionary<string, SavedSpot>(read, StringComparer.Ordinal);
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            // Starts empty; the next save rewrites it.
        }
    }

    public SavedSpot? Get(string name) => _spots.TryGetValue(name, out SavedSpot? spot) ? spot : null;

    public void Set(string name, SavedSpot spot)
    {
        _spots[name] = spot;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_file))!);
        File.WriteAllText(_file, JsonSerializer.Serialize(_spots, Options));
    }
}
