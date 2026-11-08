using Basalt.Services.Charts.Files;

namespace Basalt.Services.Charts;

public record ChartEntry(string File, ChartLoadResult Result, ChartSource Source, string? Folder = null, string? Part = null)
{
    public string Id => Part == null ? File : $"{File}/{Part}";
    public string FileName => Source == ChartSource.Folder
        ? $"{Stem}/{ChartLibrary.FolderChartFile}"
        : Part == null ? Path.GetFileName(File) : $"{Path.GetFileName(File)}/{Part}";
    public string Origin => Source == ChartSource.Folder ? $"folder {Stem}" : $"package {FileName}";

    public string AssetFolder => Folder ?? Path.GetDirectoryName(File) ?? "";
    public string? Difficulty => Result.Chart?.Info.Difficulty;
    public bool CanEdit => Result.Chart != null && Source != ChartSource.Package;
    public string ScoreKey => $"{Name}|{Result.Chart?.Info.Author}|{Difficulty?.ToLowerInvariant()}";

    public string Name => Result.Chart?.Name ?? Stem;
    public string Stem => Source == ChartSource.Folder
        ? Path.GetFileName(Path.GetDirectoryName(File)) ?? ""
        : Part == null ? Path.GetFileNameWithoutExtension(File) : $"{Path.GetFileNameWithoutExtension(File)}/{Part}";
}

public class ChartLibrary(string folder, string? packageCache = null)
{
    public string Folder => folder;
    public IReadOnlyList<ChartEntry> Entries => _entries;
    public IEnumerable<ChartEntry> Playable => _entries.Where(e => e.Result.Ok);

    public const string FolderChartFile = "chart.json";
    private static readonly string[] KnownDifficulties = { "Expert", "Hard", "Normal", "Easy" };
    private readonly string _packageCache = packageCache ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(folder)) ?? folder, "ChartCache");
    private List<ChartEntry> _entries = [];

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
            .SelectMany(Unpack);

        _entries = folders.Concat(packages).ToList();
    }

    public IReadOnlyList<ChartEntry> Songs => _entries.DistinctBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<ChartEntry> DifficultiesOf(ChartEntry entry) =>
        _entries.Where(e => string.Equals(e.Name, entry.Name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => DifficultyRank(e.Difficulty))
            .ThenBy(e => e.Difficulty, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<string> DifficultyLabels(IReadOnlyList<ChartEntry> difficulties)
    {
        var labels = difficulties.Select(e => e.Difficulty ?? e.Stem).ToList();
        labels = Disambiguate(labels, i => difficulties[i].Source == ChartSource.Package ? $"{labels[i]} (package)" : labels[i]);
        labels = Disambiguate(labels, i => $"{difficulties[i].Difficulty ?? difficulties[i].Stem} ({difficulties[i].Origin})");
        return labels;
    }

    private static List<string> Disambiguate(List<string> labels, Func<int, string> relabel)
    {
        var shared = labels.GroupBy(l => l, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return labels.Select((label, i) => shared.Contains(label) ? relabel(i) : label).ToList();
    }

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

    private IEnumerable<ChartEntry> Unpack(string package)
    {
        (var unpackedFolder, var error) = ChartPackage.Unpack(package, _packageCache);
        var chartFolders = unpackedFolder == null ? [] : ChartPackage.ChartFoldersIn(unpackedFolder);
        if (chartFolders.Count == 0)
            return [new ChartEntry(package, new ChartLoadResult(null, new[] { error ?? $"has no {FolderChartFile}" }), ChartSource.Package)];

        return chartFolders.Select(chartFolder => new ChartEntry(package,
            Read(Path.Combine(chartFolder, FolderChartFile)),
            ChartSource.Package,
            chartFolder,
            chartFolder == unpackedFolder ? null : Path.GetFileName(chartFolder)));
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
