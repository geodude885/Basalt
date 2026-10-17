namespace BossBattles.Services.Clips;

/// <summary>The clips in a folder, by the clip's name or its file name.</summary>
public class ClipLibrary
{
    private Dictionary<string, Clip> _clips = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _errors = [];

    public ClipLibrary(string folder)
    {
        Folder = folder;
    }

    public string Folder { get; }

    public IReadOnlyCollection<Clip> All => _clips.Values;

    /// <summary>Files that are not clips, with why.</summary>
    public IReadOnlyList<string> Errors => _errors;

    public void Reload()
    {
        Directory.CreateDirectory(Folder);

        var clips = new Dictionary<string, Clip>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        foreach (var file in Directory.EnumerateFiles(Folder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
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

    /// <summary>Saves a clip under a file name made from its name, never overwriting; returns the path.</summary>
    public string Save(Clip clip)
    {
        Directory.CreateDirectory(Folder);

        var stem = string.Concat(clip.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));
        var path = Enumerable.Range(1, int.MaxValue).Select(n => Path.Combine(Folder, n == 1 ? $"{stem}.json" : $"{stem}-{n}.json")).First(p => !File.Exists(p));

        File.WriteAllText(path, ClipFile.Write(clip));
        _clips[clip.Name] = clip;
        return path;
    }
}
