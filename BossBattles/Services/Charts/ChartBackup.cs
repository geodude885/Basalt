namespace BossBattles.Services.Charts;

// Every save from the game keeps the chart as it was, so the last save can be undone.
public static class ChartBackup
{
    private const string BackupExtension = ".bak";

    public static void SaveKeepingBackup(string chartFile, string previousText, string newText)
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
}
