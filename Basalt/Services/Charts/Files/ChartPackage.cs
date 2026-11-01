using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Basalt.Services.Charts.Files;

public static class ChartPackage
{
    public const string Extension = ".bbchart";
    public const string ClipsFolder = "clips";
    public const int MaxEntries = 512;
    public const long MaxUnpackedBytes = 256L * 1024 * 1024;

    private const string CompleteMarker = ".unpacked";
    private const string PartialSuffix = ".partial";
    private const int HashLength = 12;

    public static (string? Folder, string? Error) Unpack(string package, string cacheRoot)
    {
        try
        {
            var stem = Path.GetFileNameWithoutExtension(package);
            var target = Path.Combine(cacheRoot, $"{stem}-{Hash(package)}");

            if (File.Exists(Path.Combine(target, CompleteMarker))) return (target, null);

            Directory.CreateDirectory(cacheRoot);
            foreach (var stale in Directory.EnumerateDirectories(cacheRoot).Where(folder => IsOldUnpack(package, stem, folder)))
                TryDelete(stale, cacheRoot);

            using var zip = ZipFile.OpenRead(package);
            var error = ValidatePackage(zip, out var prefix);
            if (error != null) return (null, error);

            var partial = target + PartialSuffix;
            TryDelete(partial, cacheRoot);
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

    private static string? ValidatePackage(ZipArchive zip, out string prefix)
    {
        prefix = "";
        if (zip.Entries.Count > MaxEntries) return $"has {zip.Entries.Count} entries more than {MaxEntries}";
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
        return Convert.ToHexString(sha.ComputeHash(stream))[..HashLength].ToLowerInvariant();
    }

    private static bool IsUnpackOf(string stem, string folder) =>
        Regex.IsMatch(Path.GetFileName(folder), $"^{Regex.Escape(stem)}-[0-9a-f]{{{HashLength}}}({Regex.Escape(PartialSuffix)})?$", RegexOptions.None, TimeSpan.FromSeconds(1));

    private static bool IsOldUnpack(string package, string stem, string folder)
    {
        if (!IsUnpackOf(stem, folder)) return false;

        var marker = Path.Combine(folder, CompleteMarker);
        return !File.Exists(marker) || File.ReadAllText(marker) == package;
    }

    private static void TryDelete(string folder, string cacheRoot)
    {
        if (!ChartFiles.IsInsideFolder(cacheRoot, folder)) return;

        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Leave for next delete
        }
    }
}
