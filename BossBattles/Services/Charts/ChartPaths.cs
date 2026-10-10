namespace BossBattles.Services.Charts;

/// <summary>Checks on file names that come from charts and packages - other people's files.</summary>
public static class ChartPaths
{
    /// <summary>True for a relative path that stays inside its folder: not rooted, no drive, no "..".</summary>
    public static bool IsSafeRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (Path.IsPathRooted(path) || path.Contains(':', StringComparison.Ordinal)) return false;

        var parts = path.Split('/', '\\');
        return parts.All(p => p.Length > 0 && p != "." && p != "..");
    }

    /// <summary>A path inside <paramref name="folder"/>, or null when <paramref name="relative"/> is not safe.</summary>
    public static string? Inside(string folder, string relative)
    {
        if (!IsSafeRelative(relative)) return null;

        var root = Path.GetFullPath(folder);
        var full = Path.GetFullPath(Path.Combine(root, relative));
        return full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}
