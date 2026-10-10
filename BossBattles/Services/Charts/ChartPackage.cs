using System.IO.Compression;
using System.Security.Cryptography;
using BossBattles.Services.Clips;

namespace BossBattles.Services.Charts;

/// <summary>What packing a chart made, and what it could not include.</summary>
public sealed record PackResult(string Path, IReadOnlyList<string> Included, IReadOnlyList<string> Missing);

/// <summary>
/// The sharing format: a <c>.bbchart</c> is a zip of a chart folder, unpacked into a cache keyed by
/// its contents. Packages are other people's files, so entries outside the folder, too many entries
/// or too many bytes are refused.
/// </summary>
public static class ChartPackage
{
    public const string Extension = ".bbchart";
    public const string ClipsFolder = "clips";
    public const int MaxEntries = 512;
    public const long MaxUnpackedBytes = 256L * 1024 * 1024;

    private const string CompleteMarker = ".unpacked";

    /// <summary>Unpacks a package into <paramref name="cacheRoot"/> (once per distinct content); returns the folder or why not.</summary>
    public static (string? Folder, string? Error) Unpack(string package, string cacheRoot)
    {
        try
        {
            var stem = Path.GetFileNameWithoutExtension(package);
            var target = Path.Combine(cacheRoot, $"{stem}-{Hash(package)}");
            if (File.Exists(Path.Combine(target, CompleteMarker))) return (target, null);

            Directory.CreateDirectory(cacheRoot);
            foreach (var stale in Directory.EnumerateDirectories(cacheRoot, $"{stem}-*")) TryDelete(stale);

            using ZipArchive zip = ZipFile.OpenRead(package);
            var error = Check(zip, out var prefix);
            if (error != null) return (null, error);

            var partial = target + ".partial";
            TryDelete(partial);
            Directory.CreateDirectory(partial);

            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (name.EndsWith('/')) continue;

                var path = ChartPaths.Inside(partial, name[prefix.Length..]);
                if (path == null) return (null, $"has an entry \"{entry.FullName}\" outside the package");

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                entry.ExtractToFile(path, overwrite: true);
            }

            File.WriteAllText(Path.Combine(partial, CompleteMarker), package);
            Directory.Move(partial, target);
            return (target, null);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return (null, $"could not be unpacked: {e.Message}");
        }
    }

    /// <summary>
    /// Packs a chart as written, with its song, cover and clips (from its <c>clips/</c>, else
    /// <paramref name="clips"/>), overwriting <paramref name="output"/>.
    /// </summary>
    public static PackResult Pack(ChartEntry entry, string output, Func<string, Clip?>? clips = null)
    {
        Chart chart = entry.Result.Chart ?? throw new ArgumentException($"{entry.Name} does not load, so it cannot be packed", nameof(entry));
        var chartJson = File.ReadAllText(entry.Source == ChartSource.Package
            ? Path.Combine(entry.AssetFolder, ChartLibrary.FolderChartFile)
            : entry.File);

        var included = new List<string> { ChartLibrary.FolderChartFile };
        var missing = new List<string>();

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var partial = output + ".partial";
        if (File.Exists(partial)) File.Delete(partial);

        using (ZipArchive zip = ZipFile.Open(partial, ZipArchiveMode.Create))
        {
            WriteText(zip, ChartLibrary.FolderChartFile, chartJson);

            if (chart.Music != null) AddFile(zip, entry, chart.Music.File, "song", included, missing);
            if (chart.Cover != null) AddFile(zip, entry, chart.Cover, "cover", included, missing);

            foreach (var name in chart.Events.OfType<ClipEvent>().Select(c => c.Clip)
                         .Concat(chart.Tracks.SelectMany(t => t.Takes).Select(t => t.Clip))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var entryName = $"{ClipsFolder}/{FileStem(name)}.json";
                var own = ChartPaths.Inside(entry.AssetFolder, entryName);

                if (own != null && File.Exists(own))
                {
                    zip.CreateEntryFromFile(own, entryName);
                    included.Add(entryName);
                }
                else if (clips?.Invoke(name) is { } clip)
                {
                    WriteText(zip, entryName, ClipFile.Write(clip));
                    included.Add(entryName);
                }
                else
                {
                    missing.Add($"clip {name}");
                }
            }
        }

        File.Move(partial, output, overwrite: true);
        return new PackResult(output, included, missing);
    }

    /// <summary>A file the chart names, beside it - already compressed media, so stored as is.</summary>
    private static void AddFile(ZipArchive zip, ChartEntry entry, string file, string what, List<string> included, List<string> missing)
    {
        var path = ChartPaths.Inside(entry.AssetFolder, file);
        if (path == null || !File.Exists(path))
        {
            missing.Add($"{what} {file}");
            return;
        }

        zip.CreateEntryFromFile(path, file.Replace('\\', '/'), CompressionLevel.NoCompression);
        included.Add(file);
    }

    /// <summary>A file-name-safe version of a chart or clip name.</summary>
    public static string FileStem(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var stem = string.Concat(name.Trim().Select(c => invalid.Contains(c) || c == ' ' ? '-' : c));
        return stem.Length == 0 ? "chart" : stem;
    }

    /// <summary>chart.json at the top, or inside one top-level folder (a zipped folder) - which then becomes the prefix to strip.</summary>
    private static string? Check(ZipArchive zip, out string prefix)
    {
        prefix = "";
        if (zip.Entries.Count > MaxEntries) return $"has {zip.Entries.Count} entries - more than {MaxEntries}";
        if (zip.Entries.Sum(e => e.Length) > MaxUnpackedBytes) return $"unpacks to more than {MaxUnpackedBytes / (1024 * 1024)} MB";

        var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
        if (names.Contains(ChartLibrary.FolderChartFile)) return null;

        var nested = names.FirstOrDefault(n => n.Count(c => c == '/') == 1 && n.EndsWith("/" + ChartLibrary.FolderChartFile, StringComparison.Ordinal));
        if (nested == null) return $"has no {ChartLibrary.FolderChartFile} at the top";

        var folder = nested[..(nested.IndexOf('/') + 1)];
        if (!names.All(n => n.StartsWith(folder, StringComparison.Ordinal))) return $"has no {ChartLibrary.FolderChartFile} at the top";

        prefix = folder;
        return null;
    }

    private static void WriteText(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(text);
    }

    private static string Hash(string file)
    {
        using FileStream stream = File.OpenRead(file);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream))[..12].ToLowerInvariant();
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // In use (a song still loaded): left for the next unpack.
        }
        catch (UnauthorizedAccessException)
        {
            // Likewise.
        }
    }
}
