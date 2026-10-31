using System.Globalization;
using Basalt.Services.Charts;

namespace Basalt.Services.Clips;

// The clips in a folder, by the clip's name or its file name.
public class ClipLibrary(string folder)
{
    private Dictionary<string, Clip> _clips = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _errors = [];

    // Files that are not clips, with why.
    public IReadOnlyList<string> Errors => _errors;

    public void Reload()
    {
        Directory.CreateDirectory(folder);

        var clips = new Dictionary<string, Clip>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        foreach (var file in Directory.EnumerateFiles(folder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            (var clip, var error) = ClipFile.Read(File.ReadAllText(file), stem);
            if (clip == null)
            {
                errors.Add($"{Path.GetFileName(file)}: {error}");
                continue;
            }

            clips[clip.Name] = clip;
            clips.TryAdd(stem, clip);
        }

        _clips = clips;
        _errors = errors;
    }

    public Clip? Find(string name) => _clips.TryGetValue(name, out var clip) ? clip : null;

    // The actor's next clip name, counting up from its highest so far: boss-clip-1, boss-clip-2...
    public string NextName(string actorId)
    {
        var prefix = $"{ChartFiles.SanitizeFileStem(actorId)}-clip-";
        var highest = !Directory.Exists(folder) ? 0 : Directory.EnumerateFiles(folder, prefix + "*.json")
            .Select(file => int.TryParse(Path.GetFileNameWithoutExtension(file)[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();
        return $"{prefix}{highest + 1}";
    }

    // Under a file name made from the clip's name, never overwriting; returns the path.
    public string Save(Clip clip)
    {
        Directory.CreateDirectory(folder);

        var stem = ChartFiles.UniqueFileName(ChartFiles.SanitizeFileStem(clip.Name), "-", candidate => File.Exists(Path.Combine(folder, $"{candidate}.json")));
        var path = Path.Combine(folder, $"{stem}.json");
        File.WriteAllText(path, ClipFile.Write(clip));
        _clips[clip.Name] = clip;
        return path;
    }
}
