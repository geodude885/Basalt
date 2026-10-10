namespace BossBattles.Services.Charts;

/// <summary>
/// A number that changes when any file under some folders is added, removed, renamed, resized or
/// rewritten - cheap enough to check every second, so charts reload when their author saves.
/// </summary>
public static class FolderFingerprint
{
    public static long Of(params string[] folders)
    {
        var hash = new HashCode();

        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder)) continue;

            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                var info = new FileInfo(file);
                hash.Add(file, StringComparer.OrdinalIgnoreCase);
                hash.Add(info.Length);
                hash.Add(info.LastWriteTimeUtc.Ticks);
            }
        }

        // Never 0: that means "not looked yet".
        long value = hash.ToHashCode();
        return value == 0 ? 1 : value;
    }
}
