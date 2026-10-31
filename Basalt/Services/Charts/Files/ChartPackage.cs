using System.IO.Compression;
using System.Security.Cryptography;

namespace Basalt.Services.Charts.Files;

// The sharing format: a .bbchart is a zip of a chart folder, made by Basalt Editor and unpacked here into a cache
// keyed by its contents. Packages are other people's files, so entries outside the folder, too many entries or too
// many bytes are refused.
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

            using var zip = ZipFile.OpenRead(package);
            var error = Check(zip, out var prefix);
            if (error != null) return (null, error);

            var partial = target + ".partial";
            TryDelete(partial);
            Directory.CreateDirectory(partial);

            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (name.EndsWith('/')) continue;

                var path = ChartFiles.EnsureInsideFolder(partial, name[prefix.Length..]);
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

    private static string Hash(string file)
    {
        using var stream = File.OpenRead(file);
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
