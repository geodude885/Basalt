using System.IO.Compression;
using Basalt.DevTools.Testing;
using Basalt.Services.Charts;
using Basalt.Services.Charts.Files;
using Basalt.Services.Clips;
using Xunit;

namespace Basalt.Tests.Services.Charts.Files;

public sealed class ChartPackageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"Basalt-{Guid.NewGuid():N}");

    private string Charts => Path.Combine(_root, "Charts");

    private string Cache => Path.Combine(_root, "ChartCache");

    [Fact]
    public void AFolderChartIsListedAndFindsItsSongAndClips()
    {
        var folder = WriteFolderChart("my-song");

        var library = new ChartLibrary(Charts, Cache);
        library.Reload();

        var entry = Assert.Single(library.Entries);
        Assert.True(entry.Result.Ok, string.Join("; ", entry.Result.Errors));
        Assert.Equal(ChartSource.Folder, entry.Source);
        Assert.Equal("my-song", entry.Stem);
        Assert.Equal(folder, entry.AssetFolder);
        Assert.Same(entry, library.Find("my-song"));
        Assert.Same(entry, library.Find("Folder Song"));
    }

    [Fact]
    public void AZippedChartFolderDroppedInGivesTheSameChart()
    {
        var folder = WriteFolderChart("my-song");
        var package = Path.Combine(_root, "Exports", "Folder-Song" + ChartPackage.Extension);
        Directory.CreateDirectory(Path.GetDirectoryName(package)!);
        ZipFile.CreateFromDirectory(folder, package);

        // Shared: someone else drops it into their own, empty-but-for-this chart folder.
        var otherPlayersFolder = Path.Combine(_root, "Theirs", "Charts");
        Directory.CreateDirectory(otherPlayersFolder);
        File.Copy(package, Path.Combine(otherPlayersFolder, Path.GetFileName(package)));

        var otherPlayersLibrary = new ChartLibrary(otherPlayersFolder, Path.Combine(_root, "Theirs", "Cache"));
        otherPlayersLibrary.Reload();

        var entry = Assert.Single(otherPlayersLibrary.Entries);
        Assert.True(entry.Result.Ok, string.Join("; ", entry.Result.Errors));
        Assert.Equal(ChartSource.Package, entry.Source);
        Assert.Equal("Folder Song", entry.Name);
        Assert.True(File.Exists(Path.Combine(entry.AssetFolder, "song.ogg")));
        Assert.True(File.Exists(Path.Combine(entry.AssetFolder, "clips", "wave.json")));
        Assert.Contains("// kept", File.ReadAllText(Path.Combine(entry.AssetFolder, "chart.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void AZipIsAPackageToo()
    {
        var folder = WriteFolderChart("my-song");
        var zip = Path.Combine(_root, "Theirs", "Folder-Song.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
        ZipFile.CreateFromDirectory(folder, zip);

        var otherPlayersFolder = Path.GetDirectoryName(zip)!;
        var otherPlayersLibrary = new ChartLibrary(otherPlayersFolder, Path.Combine(_root, "TheirCache"));
        otherPlayersLibrary.Reload();

        Assert.Equal("Folder Song", Assert.Single(otherPlayersLibrary.Playable).Name);
    }

    [Fact]
    public void AZippedFolderIsAcceptedAsAPackage()
    {
        Directory.CreateDirectory(Charts);
        var package = Path.Combine(Charts, "zipped.bbchart");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            Write(zip, "My Chart/chart.json", /*lang=json,strict*/ """{ "name": "Zipped" }""");
            Write(zip, "My Chart/notes.txt", "hi");
        }

        var library = new ChartLibrary(Charts, Cache);
        library.Reload();

        var entry = Assert.Single(library.Entries);
        Assert.Equal("Zipped", entry.Name);
        Assert.True(File.Exists(Path.Combine(entry.AssetFolder, "notes.txt")));
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("clips/../../evil.txt")]
    public void APackageReachingOutsideItsFolderIsRefused(string evil)
    {
        Directory.CreateDirectory(Charts);
        var package = Path.Combine(Charts, "evil.bbchart");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            Write(zip, "chart.json", /*lang=json,strict*/ """{ "name": "Evil" }""");
            Write(zip, evil, "gotcha");
        }

        var library = new ChartLibrary(Charts, Cache);
        library.Reload();

        var entry = Assert.Single(library.Entries);
        Assert.False(entry.Result.Ok);
        Assert.Contains("outside the package", entry.Result.Errors[0], StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(Cache, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")));
    }

    [Fact]
    public void APackageWithoutAChartIsListedWithWhy()
    {
        Directory.CreateDirectory(Charts);
        using (var zip = ZipFile.Open(Path.Combine(Charts, "empty.bbchart"), ZipArchiveMode.Create))
            Write(zip, "readme.txt", "no chart here");

        var library = new ChartLibrary(Charts, Cache);
        library.Reload();

        Assert.Contains("no chart.json", Assert.Single(library.Entries).Result.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnchangedPackageIsUnpackedOnce()
    {
        var folder = WriteFolderChart("my-song");
        var package = Path.Combine(Charts, "shared.bbchart");
        ZipFile.CreateFromDirectory(folder, package);

        (var first, _) = ChartPackage.Unpack(package, Cache);
        File.WriteAllText(Path.Combine(first!, "marker"), "still here");
        (var second, _) = ChartPackage.Unpack(package, Cache);

        Assert.Equal(first, second);
        Assert.True(File.Exists(Path.Combine(second!, "marker")));
    }

    [Fact]
    public void UnpackingLeavesAPackageWithALongerNameAlone()
    {
        (var hard, _) = ChartPackage.Unpack(WritePackage("song-hard", "Hard"), Cache);
        (var easy, _) = ChartPackage.Unpack(WritePackage("song", "Easy"), Cache);

        Assert.True(Directory.Exists(hard));
        Assert.True(Directory.Exists(easy));
    }

    [Fact]
    public void AChangedPackageReplacesItsOldUnpack()
    {
        (var before, _) = ChartPackage.Unpack(WritePackage("song", "Before"), Cache);
        (var after, _) = ChartPackage.Unpack(WritePackage("song", "After"), Cache);

        Assert.NotEqual(before, after);
        Assert.False(Directory.Exists(before));
        Assert.True(Directory.Exists(after));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string WriteFolderChart(string name)
    {
        var folder = Path.Combine(Charts, name);
        Directory.CreateDirectory(Path.Combine(folder, ChartPackage.ClipsFolder));
        File.WriteAllText(Path.Combine(folder, "chart.json"), /*lang=json*/ """
            {
              // kept: comments survive zipping
              "name": "Folder Song",
              "music": { "file": "song.ogg" },
              "cover": "cover.png",
              "format": 2,
              "performances": [ { "clips": [ { "t": 1, "clip": "wave" } ] } ]
            }
            """);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tone.ogg"), Path.Combine(folder, "song.ogg"));
        File.WriteAllBytes(Path.Combine(folder, "cover.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        File.WriteAllText(Path.Combine(folder, ChartPackage.ClipsFolder, "wave.json"), ClipFile.Write(SyntheticClips.Sway("wave", 1f)));
        return folder;
    }

    private string WritePackage(string stem, string name)
    {
        Directory.CreateDirectory(Charts);
        var package = Path.Combine(Charts, stem + ChartPackage.Extension);
        File.Delete(package);
        using var zip = ZipFile.Open(package, ZipArchiveMode.Create);
        Write(zip, "chart.json", $$"""{ "name": "{{name}}" }""");
        return package;
    }

    private static void Write(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(text);
    }
}
