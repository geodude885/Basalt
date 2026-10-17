namespace BossBattles.Services.Charts;

public enum ChartSource
{
    /// <summary>A lone <c>name.json</c>, its songs and clips beside it.</summary>
    File,

    /// <summary>A folder holding <c>chart.json</c>, its songs and <c>clips/</c>.</summary>
    Folder,

    /// <summary>A <c>.bbchart</c> package: the folder zipped, for sharing.</summary>
    Package,
}

/// <summary>One chart and what loading it gave.</summary>
/// <param name="File">The .json, the folder's chart.json, or the .bbchart.</param>
/// <param name="Result">The chart, or what is wrong with it.</param>
/// <param name="Source">How it is stored.</param>
/// <param name="Folder">Where its songs and clips are; for a package, where it was unpacked.</param>
public record ChartEntry(string File, ChartLoadResult Result, ChartSource Source = ChartSource.File, string? Folder = null)
{
    /// <summary>The chart's name, or its file or folder name when it does not load.</summary>
    public string Name => Result.Chart?.Name ?? Stem;

    /// <summary>The file or folder name without extension.</summary>
    public string Stem => Source == ChartSource.Folder
        ? Path.GetFileName(Path.GetDirectoryName(File)) ?? ""
        : Path.GetFileNameWithoutExtension(File);

    public string AssetFolder => Folder ?? Path.GetDirectoryName(File) ?? "";

    public bool CanEdit => Result.Chart != null && Source != ChartSource.Package;

    public bool HasActorTrack => CanEdit && Result.Chart!.Tracks.Any(t => t.ActorId == Result.Chart.DefaultActor.Id && t.Clips.Count > 0);

    /// <summary>A best-score key that survives renaming the file.</summary>
    public string ScoreKey => $"{Name}|{Result.Chart?.Info.Author}";
}

/// <summary>
/// The charts in a folder: loose <c>*.json</c>, chart folders and <c>*.bbchart</c> packages. An
/// empty folder gets the starter charts, and starters still exactly as written are kept up to date.
/// </summary>
public class ChartLibrary
{
    public const string FolderChartFile = "chart.json";
    private List<ChartEntry> _entries = [];

    /// <param name="folder">The chart folder.</param>
    /// <param name="cache">Where packages are unpacked; default "ChartCache" beside <paramref name="folder"/>.</param>
    public ChartLibrary(string folder, string? cache = null)
    {
        Folder = folder;
        Cache = cache ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(folder)) ?? folder, "ChartCache");
    }

    public string Folder { get; }

    public string Cache { get; }

    /// <summary>Every chart, loaded or not: files, folders, then packages, each in name order.</summary>
    public IReadOnlyList<ChartEntry> Entries => _entries;

    public IEnumerable<ChartEntry> Playable => _entries.Where(e => e.Result.Ok);

    /// <returns>How many starter charts were written (0 when the folder already had charts).</returns>
    public int Reload()
    {
        Directory.CreateDirectory(Folder);

        var hasCharts = Directory.EnumerateFiles(Folder, "*.json").Any()
                        || Packages().Any()
                        || Directory.EnumerateDirectories(Folder).Any(d => File.Exists(Path.Combine(d, FolderChartFile)));
        var written = hasCharts ? UpdateStarters() : WriteStarters();

        var entries = new List<ChartEntry>();

        entries.AddRange(Directory.EnumerateFiles(Folder, "*.json")
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .Select(f => new ChartEntry(f, Read(f), ChartSource.File)));

        entries.AddRange(Directory.EnumerateDirectories(Folder)
            .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
            .Select(d => Path.Combine(d, FolderChartFile))
            .Where(File.Exists)
            .Select(f => new ChartEntry(f, Read(f), ChartSource.Folder)));

        entries.AddRange(Packages()
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .Select(Unpack));

        _entries = entries;
        return written;
    }

    public ChartEntry? Find(string name) =>
        _entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? _entries.FirstOrDefault(e => string.Equals(e.Stem, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Each starter chart's hash as written, so a newer mod can tell it was never edited.</summary>
    private const string StartersRecord = ".starters";

    private int WriteStarters()
    {
        var record = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((var file, var json) in StarterCharts.All)
        {
            File.WriteAllText(Path.Combine(Folder, file), json);
            record[file] = Hash(json);
        }

        SaveRecord(record);
        return StarterCharts.All.Count;
    }

    /// <summary>Brings starter charts still exactly as written up to date; edited or deleted ones are left alone.</summary>
    private int UpdateStarters()
    {
        var record = LoadRecord();
        var written = 0;

        foreach ((var file, var json) in StarterCharts.All)
        {
            var path = Path.Combine(Folder, file);
            if (!record.TryGetValue(file, out var wrote) || !File.Exists(path)) continue;

            var current = File.ReadAllText(path);
            if (Hash(current) != wrote || current == json) continue;

            File.WriteAllText(path, json);
            record[file] = Hash(json);
            written++;
        }

        if (written > 0) SaveRecord(record);
        return written;
    }

    private Dictionary<string, string> LoadRecord()
    {
        var record = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(Folder, StartersRecord);
        if (!File.Exists(path)) return record;

        foreach (var line in File.ReadAllLines(path))
        {
            var parts = line.Split(' ', 2);
            if (parts.Length == 2) record[parts[1]] = parts[0];
        }

        return record;
    }

    private void SaveRecord(Dictionary<string, string> record) =>
        File.WriteAllLines(Path.Combine(Folder, StartersRecord), record.Select(r => $"{r.Value} {r.Key}"));

    private static string Hash(string text) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n"))))[..16];

    /// <summary>Packages: <c>.bbchart</c>, or <c>.zip</c> as the hosted editor saves them.</summary>
    private IEnumerable<string> Packages() =>
        Directory.EnumerateFiles(Folder, "*" + ChartPackage.Extension).Concat(Directory.EnumerateFiles(Folder, "*.zip"));

    private ChartEntry Unpack(string package)
    {
        (var folder, var error) = ChartPackage.Unpack(package, Cache);
        return folder == null
            ? new ChartEntry(package, new ChartLoadResult(null, new[] { error ?? "could not be unpacked" }), ChartSource.Package)
            : new ChartEntry(package, Read(Path.Combine(folder, FolderChartFile)), ChartSource.Package, folder);
    }

    private static ChartLoadResult Read(string file)
    {
        try
        {
            return ChartLoader.Load(File.ReadAllText(file));
        }
        catch (IOException e)
        {
            return new ChartLoadResult(null, new[] { $"could not read {Path.GetFileName(file)}: {e.Message}" });
        }
    }
}
