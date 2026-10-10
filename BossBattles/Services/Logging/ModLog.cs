namespace BossBattles.Services.Logging;

/// <summary>
/// A log that survives a native crash: each line is written through to disk before the call returns.
/// MelonLoader's own log is buffered and loses exactly the last lines.
/// </summary>
public sealed class ModLog : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _gate = new();

    public ModLog(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        _writer = new StreamWriter(stream) { AutoFlush = true };
    }

    public void Line(string message)
    {
        lock (_gate)
        {
            _writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {message}");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer.Dispose();
        }
    }
}
