namespace BossBattles.Services.Charts;

/// <summary>How a chart is stored.</summary>
public enum ChartSource
{
    /// <summary>A lone <c>name.json</c> in the chart folder. Its songs and clips sit beside it.</summary>
    File,

    /// <summary>A folder holding <c>chart.json</c> and its own songs and <c>clips/</c> - for writing a chart.</summary>
    Folder,

    /// <summary>A <c>.bbchart</c> package - the same folder zipped, for sharing (see <see cref="ChartPackage"/>).</summary>
    Package,
}

/// <summary>One chart and what loading it gave.</summary>
/// <param name="File">Where it came from: the .json, the folder's chart.json, or the .bbchart.</param>
/// <param name="Result">The chart, or what is wrong with it.</param>
/// <param name="Source">How it is stored.</param>
/// <param name="Folder">Where its songs and clips are looked up - for a package, where it was unpacked.</param>
public sealed record ChartEntry(string File, ChartLoadResult Result, ChartSource Source = ChartSource.File, string? Folder = null)
{
    /// <summary>The chart's own name when it loaded, the file (or folder) name when it did not.</summary>
    public string Name => Result.Chart?.Name ?? Stem;

    /// <summary>The file or folder name without extension - also accepted by <see cref="ChartLibrary.Find"/>.</summary>
    public string Stem => Source == ChartSource.Folder
        ? Path.GetFileName(Path.GetDirectoryName(File)) ?? ""
        : Path.GetFileNameWithoutExtension(File);

    public string AssetFolder => Folder ?? Path.GetDirectoryName(File) ?? "";

    /// <summary>A key for best scores that survives renaming the file: name and author.</summary>
    public string ScoreKey => $"{Name}|{Result.Chart?.Info.Author}";
}

/// <summary>
/// The charts in a folder (<c>UserData/BossBattles/Charts</c> in the game): loose <c>*.json</c>
/// files, chart folders (<c>*/chart.json</c>) and <c>*.bbchart</c> packages, which are unpacked
/// into a cache folder. An empty or missing folder is given the starter charts, so there is always
/// something to play; later, starter charts still exactly as written are brought up to date, and
/// nothing else in the folder is ever written to.
/// </summary>
public sealed class ChartLibrary
{
    public const string FolderChartFile = "chart.json";
    private List<ChartEntry> _entries = [];

    /// <param name="folder">The chart folder.</param>
    /// <param name="cache">Where packages are unpacked; by default a "ChartCache" folder beside <paramref name="folder"/>.</param>
    public ChartLibrary(string folder, string? cache = null)
    {
        Folder = folder;
        Cache = cache ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(folder)) ?? folder, "ChartCache");
    }

    public string Folder { get; }

    public string Cache { get; }

    /// <summary>Every chart found, loaded or not: files, then folders, then packages, each in name order.</summary>
    public IReadOnlyList<ChartEntry> Entries => _entries;

    public IEnumerable<ChartEntry> Playable => _entries.Where(e => e.Result.Ok);

    /// <returns>How many starter charts were written (0 when the folder already had charts).</returns>
    public int Reload()
    {
        Directory.CreateDirectory(Folder);

        var written = HasAnyChart() ? UpdateStarters() : WriteStarters();

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

    /// <summary>Remembers each starter chart as written (file and content hash), so a newer mod can tell it was never edited.</summary>
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

    /// <summary>
    /// Brings starter charts up to date - only those still exactly as the mod wrote them. One you
    /// edited, or deleted, is left alone.
    /// </summary>
    private int UpdateStarters()
    {
        Dictionary<string, string> record = LoadRecord();
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

    private bool HasAnyChart() =>
        Directory.EnumerateFiles(Folder, "*.json").Any()
        || Packages().Any()
        || Directory.EnumerateDirectories(Folder).Any(d => File.Exists(Path.Combine(d, FolderChartFile)));

    /// <summary>Packages: <c>.bbchart</c>, or a <c>.zip</c> - what the hosted editor saves, where only zips can be offered.</summary>
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
