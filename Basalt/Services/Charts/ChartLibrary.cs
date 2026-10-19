namespace Basalt.Services.Charts;

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
public class ChartLibrary(string folder, string? packageCache = null)
{
    public const string FolderChartFile = "chart.json";

    // Each starter chart's hash when it was written, so a newer mod can tell one was never edited.
    private const string StarterHashesFile = ".starters";

    // Where packages are unpacked: "ChartCache" beside the chart folder, unless given.
    private readonly string _packageCache = packageCache ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(folder)) ?? folder, "ChartCache");

    private List<ChartEntry> _entries = [];

    public string Folder => folder;

    // Every chart, loaded or not: files, folders, then packages, each in name order.
    public IReadOnlyList<ChartEntry> Entries => _entries;

    public IEnumerable<ChartEntry> Playable => _entries.Where(e => e.Result.Ok);

    // Returns how many starter charts were written.
    public int Reload()
    {
        Directory.CreateDirectory(folder);

        var folderIsEmpty = !Directory.EnumerateFiles(folder, "*.json").Any()
                            && !Packages().Any()
                            && !Directory.EnumerateDirectories(folder).Any(d => File.Exists(Path.Combine(d, FolderChartFile)));
        var startersWritten = WriteStarterCharts(folderIsEmpty);

        var files = Directory.EnumerateFiles(folder, "*.json")
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(file => new ChartEntry(file, Read(file), ChartSource.File));
        var folders = Directory.EnumerateDirectories(folder)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(chartFolder => Path.Combine(chartFolder, FolderChartFile))
            .Where(File.Exists)
            .Select(file => new ChartEntry(file, Read(file), ChartSource.Folder));
        var packages = Packages()
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(Unpack);

        _entries = files.Concat(folders).Concat(packages).ToList();
        return startersWritten;
    }

    public ChartEntry? Find(string name) =>
        _entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? _entries.FirstOrDefault(e => string.Equals(e.Stem, name, StringComparison.OrdinalIgnoreCase));

    // An empty folder gets every starter chart. Otherwise a starter still exactly as it was written is brought up
    // to date, and one that was edited or deleted is left alone.
    private int WriteStarterCharts(bool folderIsEmpty)
    {
        var hashesFile = Path.Combine(folder, StarterHashesFile);
        var hashWhenWritten = folderIsEmpty || !File.Exists(hashesFile)
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : File.ReadAllLines(hashesFile)
                .Select(line => line.Split(' ', 2))
                .Where(parts => parts.Length == 2)
                .DistinctBy(parts => parts[1], StringComparer.OrdinalIgnoreCase)
                .ToDictionary(parts => parts[1], parts => parts[0], StringComparer.OrdinalIgnoreCase);

        var written = 0;
        foreach ((var file, var json) in StarterCharts.All)
        {
            var path = Path.Combine(folder, file);
            var current = File.Exists(path) ? File.ReadAllText(path) : null;
            var untouchedSinceWritten = current != null && hashWhenWritten.TryGetValue(file, out var hash) && Hash(current) == hash;
            if (!folderIsEmpty && (!untouchedSinceWritten || current == json)) continue;

            File.WriteAllText(path, json);
            hashWhenWritten[file] = Hash(json);
            written++;
        }

        if (written > 0) File.WriteAllLines(hashesFile, hashWhenWritten.Select(entry => $"{entry.Value} {entry.Key}"));
        return written;
    }

    private static string Hash(string text) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n"))))[..16];

    // Packages: .bbchart, or .zip as the hosted editor saves them.
    private IEnumerable<string> Packages() =>
        Directory.EnumerateFiles(folder, "*" + ChartPackage.Extension).Concat(Directory.EnumerateFiles(folder, "*.zip"));

    private ChartEntry Unpack(string package)
    {
        (var unpackedFolder, var error) = ChartPackage.Unpack(package, _packageCache);
        return unpackedFolder == null
            ? new ChartEntry(package, new ChartLoadResult(null, new[] { error ?? "could not be unpacked" }), ChartSource.Package)
            : new ChartEntry(package, Read(Path.Combine(unpackedFolder, FolderChartFile)), ChartSource.Package, unpackedFolder);
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
