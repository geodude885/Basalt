namespace Basalt.Services.Charts;

public static class ChartFiles
{
    private const string BackupExtension = ".bak";

    public static string SanitizeFileStem(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var stem = string.Concat(name.Trim().Select(c => invalid.Contains(c) || c == ' ' ? '-' : c));
        return stem.Length == 0 ? "chart" : stem;
    }

    public static string UniqueFileName(string name, string separator, Func<string, bool> taken) =>
        Enumerable.Range(1, int.MaxValue).Select(n => n == 1 ? name : $"{name}{separator}{n}").First(candidate => !taken(candidate));

    public static bool IsSafeRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (Path.IsPathRooted(path) || path.Contains(':', StringComparison.Ordinal)) return false;

        return path.Split('/', '\\').All(part => part.Length > 0 && part != "." && part != "..");
    }

    public static string? EnsureInsideFolder(string folder, string relative)
    {
        if (!IsSafeRelative(relative)) return null;

        var root = Path.GetFullPath(folder);
        var full = Path.GetFullPath(Path.Combine(root, relative));
        return full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    public static void BackupAndSave(string chartFile, string previousText, string newText)
    {
        File.WriteAllText(chartFile + BackupExtension, previousText);
        File.WriteAllText(chartFile, newText);
    }

    public static bool HasBackup(string chartFile) => File.Exists(chartFile + BackupExtension);

    public static bool RestoreBackup(string chartFile)
    {
        var backup = chartFile + BackupExtension;
        if (!File.Exists(backup)) return false;

        File.Copy(backup, chartFile, overwrite: true);
        File.Delete(backup);
        return true;
    }

    public static long Fingerprint(params string[] folders)
    {
        var hash = new HashCode();
        foreach (var file in folders.Where(Directory.Exists).SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)))
        {
            var info = new FileInfo(file);
            hash.Add(file, StringComparer.OrdinalIgnoreCase);
            hash.Add(info.Length);
            hash.Add(info.LastWriteTimeUtc.Ticks);
        }

        long value = hash.ToHashCode();
        return value == 0 ? 1 : value;
    }
}
