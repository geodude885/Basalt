using System.Text;

namespace Basalt.Services;

public static class SafeFile
{
    // Save via a temp file to avoid corruption if crashing mid-save
    public static void WriteAllText(string path, string text)
    {
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            writer.Write(text);
        }

        File.Move(temp, path, overwrite: true);
    }
}
