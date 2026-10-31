namespace Basalt.Services.Charts;

public record ChartEntry(string File, ChartLoadResult Result, ChartSource Source, string? Folder = null)
{
    public string AssetFolder => Folder ?? Path.GetDirectoryName(File) ?? "";
    public string? Difficulty => Result.Chart?.Info.Difficulty;
    public bool CanEdit => Result.Chart != null && Source != ChartSource.Package;
    public string ScoreKey => $"{Name}|{Result.Chart?.Info.Author}";

    public string Name => Result.Chart?.Name ?? Stem;
    public string Stem => Source == ChartSource.Folder
        ? Path.GetFileName(Path.GetDirectoryName(File)) ?? ""
        : Path.GetFileNameWithoutExtension(File);
}

public class ChartLibrary(string folder, string? packageCache = null)
{
    public const string FolderChartFile = "chart.json";

    private static readonly string[] KnownDifficulties = { "Expert", "Hard", "Normal", "Easy" };

    private readonly string _packageCache = packageCache ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(folder)) ?? folder, "ChartCache");

    private List<ChartEntry> _entries = [];

    public string Folder => folder;

    public IReadOnlyList<ChartEntry> Entries => _entries;
    public IEnumerable<ChartEntry> Playable => _entries.Where(e => e.Result.Ok);

    public void Reload()
    {
        Directory.CreateDirectory(folder);

        var folders = Directory.EnumerateDirectories(folder)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(chartFolder => Path.Combine(chartFolder, FolderChartFile))
            .Where(File.Exists)
            .Select(file => new ChartEntry(file, Read(file), ChartSource.Folder));
        var packages = Packages()
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(Unpack);

        _entries = folders.Concat(packages).ToList();
    }

    public IReadOnlyList<ChartEntry> Songs => _entries.DistinctBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<ChartEntry> DifficultiesOf(ChartEntry entry) =>
        _entries.Where(e => string.Equals(e.Name, entry.Name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => DifficultyRank(e.Difficulty))
            .ThenBy(e => e.Difficulty, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public ChartEntry? Find(string name) =>
        _entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? _entries.FirstOrDefault(e => string.Equals(e.Stem, name, StringComparison.OrdinalIgnoreCase));

    private static int DifficultyRank(string? difficulty)
    {
        if (difficulty == null) return int.MaxValue;
        var known = Array.FindIndex(KnownDifficulties, d => string.Equals(d, difficulty, StringComparison.OrdinalIgnoreCase));
        return known >= 0 ? known : KnownDifficulties.Length;
    }

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

public enum ChartSource
{
    Folder,
    Package
}
