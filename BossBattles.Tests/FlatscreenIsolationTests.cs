using System.Text.RegularExpressions;
using Xunit;

namespace BossBattles.Tests;

/// <summary>
/// <c>Flatscreen/</c> is meant to lift out into its own mod, so nothing in it may reach into the rest
/// of the lab. Reads the source rather than reflecting over signatures, because the easy mistake is
/// a call buried in a method body - a <c>Lab.Note(...)</c> instead of the injected log callback.
/// </summary>
public sealed class FlatscreenIsolationTests
{
    [Fact]
    public void FlatscreenReferencesNothingElseInTheLab()
    {
        var folder = Path.Combine(SolutionRoot(), "BossBattles", "Flatscreen");
        var offenders = new List<string>();

        foreach (var file in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (LabReference.IsMatch(lines[i]))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Flatscreen/ must stay self-contained so it can become its own mod:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    // Another lab namespace, or the lab's static entry points. (GeneratedRegex needs .NET 7; tests are net6.0.)
    private static readonly Regex LabReference =
        new(@"BossBattles\.(?!Flatscreen\b)\w+|\bMod\.|\bModEntry\b|\bActor\b", RegexOptions.Compiled);


    private static string SolutionRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (dir.GetFiles("*.slnx").Length > 0) return dir.FullName;
        }

        throw new InvalidOperationException("solution root not found above " + AppContext.BaseDirectory);
    }
}
