using Basalt.Services.Charts;
using Basalt.Services.Charts.Files;
using Xunit;

namespace Basalt.Tests.Services.Charts.Files;

public sealed class ChartAuthoringTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"Basalt-{Guid.NewGuid():N}");

    [Fact]
    public void NewChartsGetTheirOwnFolderAndAreListed()
    {
        Directory.CreateDirectory(_folder);
        var first = ChartTemplate.Create(_folder, "My Chart", "me");
        var second = ChartTemplate.Create(_folder, "My Chart", "me");

        Assert.NotEqual(first, second);
        var library = new ChartLibrary(_folder, Path.Combine(_folder, "..", "cache-" + Guid.NewGuid().ToString("N")));
        library.Reload();
        Assert.Equal(2, library.Playable.Count());
        Assert.True(Directory.Exists(Path.Combine(first, ChartPackage.ClipsFolder)));
    }

    [Fact]
    public void TheFingerprintChangesWhenAChartIsSaved()
    {
        Directory.CreateDirectory(_folder);
        var file = Path.Combine(_folder, "a.json");
        File.WriteAllText(file, "{}");
        var before = ChartFiles.Fingerprint(_folder);

        Assert.Equal(before, ChartFiles.Fingerprint(_folder));
        File.WriteAllText(file, /*lang=json,strict*/ "{ \"name\": \"changed\" }");
        Assert.NotEqual(before, ChartFiles.Fingerprint(_folder));
        Assert.NotEqual(0, ChartFiles.Fingerprint(Path.Combine(_folder, "missing")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
