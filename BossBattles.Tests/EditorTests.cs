using Xunit;

namespace BossBattles.Tests;

public sealed class EditorTests
{
    /// <summary>The editor the mod ships is built from editor/index.html - this catches forgetting to rebuild it.</summary>
    [Fact]
    public void TheBundledEditorIsUpToDate()
    {
        var root = Root();
        var source = File.ReadAllText(Path.Combine(root, "editor", "index.html"));
        var bundled = File.ReadAllText(Path.Combine(root, "BossBattles", "Editor", "BossBattlesEditor.html"));

        Assert.True(bundled.Contains(source.Trim(), StringComparison.Ordinal), "BossBattles/Editor/BossBattlesEditor.html is stale - run: python tools/build-editor.py");
    }

    [Fact]
    public void TheModCarriesTheEditor() =>
        Assert.NotNull(typeof(ModEntry).Assembly.GetManifestResourceStream("BossBattles.ChartEditor.html"));

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BossBattles.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
