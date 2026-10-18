using System.IO.Compression;
using BossBattles.DevTools.Testing;
using BossBattles.Services;
using BossBattles.Services.Audio;
using BossBattles.Services.Charts;
using BossBattles.Services.Clips;
using BossBattles.Services.Scoring;
using Xunit;

namespace BossBattles.Tests;

public class ChartHeaderTests
{
    [Fact]
    public void ReadsInfoArenaAndMusic()
    {
        var chart = Load(/*lang=json,strict*/ """
            {
              "format": 1, "name": "Song", "author": "someone", "difficulty": "Hard", "description": "A song.",
              "arena": { "map": "Ring", "at": { "x": 1.5, "z": -6 }, "facing": 90 },
              "music": { "file": "music/song.ogg", "start": 2.5, "volume": 0.7 }
            }
            """);

        Assert.Equal(new ChartInfo("someone", "A song.", "Hard"), chart.Info);
        Assert.Equal(new ArenaSpec(ArenaKind.Ring, 1.5f, -6f, 90f), chart.Arena);
        Assert.True(chart.Arena.HasSpot);
        Assert.Equal(new MusicSpec("music/song.ogg", 2.5, 0.7f), chart.Music);
    }

    [Fact]
    public void ReadsACover() => Assert.Equal("art/cover.png", Load(/*lang=json,strict*/ """{ "cover": "art/cover.png" }""").Cover);

    [Fact]
    public void TheNewChartTemplatePlaysAsItIs()
    {
        var result = ChartLoader.Load(ChartTemplate.Json("Quote \" test", "someone"));

        Assert.True(result.Ok, string.Join("; ", result.Errors));
        Assert.Equal("Quote \" test", result.Chart!.Name);
        Assert.Equal("someone", result.Chart.Info.Author);
        Assert.NotEmpty(result.Chart.Events.OfType<ProjectileEvent>());
    }

    [Fact]
    public void WithoutAHeaderAChartIsPlayedHereInSilence()
    {
        var chart = Load(/*lang=json,strict*/ """{ "name": "Plain" }""");

        Assert.Equal(ArenaSpec.Here, chart.Arena);
        Assert.Null(chart.Music);
        Assert.Equal(ChartInfo.None, chart.Info);
    }

    [Fact]
    public void AMapWithoutASpotIsLaidOutAutomatically()
    {
        var arena = Load(/*lang=json,strict*/ """{ "arena": { "map": "pit" } }""").Arena;

        Assert.Equal(ArenaKind.Pit, arena.Map);
        Assert.False(arena.HasSpot);
    }

    [Theory]
    [InlineData(/*lang=json,strict*/ """{ "format": 3 }""", "newer BossBattles")]
    [InlineData(/*lang=json,strict*/ """{ "arena": { "map": "moon" } }""", "arena.map \"moon\"")]
    [InlineData(/*lang=json,strict*/ """{ "arena": { "map": "ring", "at": { "x": 1 } } }""", "arena.at needs x and z")]
    [InlineData(/*lang=json,strict*/ """{ "arena": { "at": { "x": 1, "z": 1 } } }""", "need a map")]
    [InlineData(/*lang=json,strict*/ """{ "music": { "start": 1 } }""", "music.file is required")]
    [InlineData(/*lang=json,strict*/ """{ "music": { "file": "../../secret.ogg" } }""", "inside the chart's folder")]
    [InlineData(/*lang=json,strict*/ """{ "music": { "file": "C:/music/song.ogg" } }""", "inside the chart's folder")]
    [InlineData(/*lang=json,strict*/ """{ "music": { "file": "song.flac" } }""", "must be one of")]
    [InlineData(/*lang=json,strict*/ """{ "music": { "file": "song.ogg", "volume": 2 } }""", "music.volume")]
    [InlineData(/*lang=json,strict*/ """{ "music": { "file": "song.ogg", "start": -1 } }""", "music.start")]
    [InlineData(/*lang=json,strict*/ """{ "cover": "../cover.png" }""", "inside the chart's folder")]
    [InlineData(/*lang=json,strict*/ """{ "cover": "cover.gif" }""", "must be one of")]
    public void BadHeadersAreReported(string json, string expected)
    {
        var result = ChartLoader.Load(json);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownFieldsAreIgnoredSoNewerChartsStillLoad() => Assert.True(ChartLoader.Load(/*lang=json,strict*/ """{ "$schema": "chart.schema.json", "weather": { "sky": "red" } }""").Ok);

    [Fact]
    public void EveryStarterChartNamesItsAuthorAndDifficulty()
    {
        foreach ((var file, var json) in StarterCharts.All)
        {
            var chart = ChartLoader.Load(json).Chart ?? throw new InvalidOperationException(file);
            Assert.NotNull(chart.Info.Author);
            Assert.NotNull(chart.Info.Difficulty);
        }
    }

    private static Chart Load(string json)
    {
        var result = ChartLoader.Load(json);
        return result.Chart ?? throw new InvalidOperationException(string.Join("; ", result.Errors));
    }
}

public class ChartPathTests
{
    [Theory]
    [InlineData("song.ogg", true)]
    [InlineData("music/song.ogg", true)]
    [InlineData("music\\song.ogg", true)]
    [InlineData("../song.ogg", false)]
    [InlineData("music/../../song.ogg", false)]
    [InlineData("/song.ogg", false)]
    [InlineData("C:/song.ogg", false)]
    [InlineData("C:song.ogg", false)]
    [InlineData("", false)]
    [InlineData("music//song.ogg", false)]
    public void OnlyPathsInsideTheFolderAreSafe(string path, bool safe) =>
        Assert.Equal(safe, ChartFiles.IsSafeRelative(path));

    [Fact]
    public void InsideResolvesWithinTheFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "charts");

        Assert.Equal(Path.GetFullPath(Path.Combine(folder, "a", "b.ogg")), ChartFiles.Inside(folder, "a/b.ogg"));
        Assert.Null(ChartFiles.Inside(folder, "../b.ogg"));
    }
}

public sealed class ChartPackageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"BossBattles-{Guid.NewGuid():N}");

    private string Charts => Path.Combine(_root, "Charts");

    private string Cache => Path.Combine(_root, "ChartCache");

    [Fact]
    public void AFolderChartIsListedAndFindsItsSongAndClips()
    {
        var folder = WriteFolderChart("my-song");

        var library = new ChartLibrary(Charts, Cache);
        Assert.Equal(0, library.Reload());

        var entry = Assert.Single(library.Entries);
        Assert.True(entry.Result.Ok, string.Join("; ", entry.Result.Errors));
        Assert.Equal(ChartSource.Folder, entry.Source);
        Assert.Equal("my-song", entry.Stem);
        Assert.Equal(folder, entry.AssetFolder);
        Assert.Same(entry, library.Find("my-song"));
        Assert.Same(entry, library.Find("Folder Song"));
    }

    [Fact]
    public void PackingAndDroppingThePackageInGivesTheSameChart()
    {
        WriteFolderChart("my-song");
        var library = new ChartLibrary(Charts, Cache);
        library.Reload();

        var package = Path.Combine(_root, "Exports", "Folder-Song" + ChartPackage.Extension);
        var packed = ChartPackage.Pack(library.Entries[0], package);

        Assert.Empty(packed.Missing);
        Assert.Equal(new[] { "chart.json", "song.wav", "cover.png", "clips/wave.json" }, packed.Included);

        // Shared: someone else drops it into their own, empty-but-for-this chart folder.
        var theirs = Path.Combine(_root, "Theirs", "Charts");
        Directory.CreateDirectory(theirs);
        File.Copy(package, Path.Combine(theirs, Path.GetFileName(package)));

        var their = new ChartLibrary(theirs, Path.Combine(_root, "Theirs", "Cache"));
        Assert.Equal(0, their.Reload());

        var entry = Assert.Single(their.Entries);
        Assert.True(entry.Result.Ok, string.Join("; ", entry.Result.Errors));
        Assert.Equal(ChartSource.Package, entry.Source);
        Assert.Equal("Folder Song", entry.Name);
        Assert.True(File.Exists(Path.Combine(entry.AssetFolder, "song.wav")));
        Assert.True(File.Exists(Path.Combine(entry.AssetFolder, "clips", "wave.json")));
        Assert.Contains("// kept", File.ReadAllText(Path.Combine(entry.AssetFolder, "chart.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void AZipIsAPackageToo()
    {
        WriteFolderChart("my-song");
        var library = new ChartLibrary(Charts, Cache);
        library.Reload();
        var zip = Path.Combine(_root, "Theirs", "Folder-Song.zip");
        ChartPackage.Pack(library.Entries[0], zip);

        var theirs = Path.GetDirectoryName(zip)!;
        var their = new ChartLibrary(theirs, Path.Combine(_root, "TheirCache"));
        their.Reload();

        Assert.Equal("Folder Song", Assert.Single(their.Playable).Name);
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
        WriteFolderChart("my-song");
        var library = new ChartLibrary(Charts, Cache);
        library.Reload();
        var package = Path.Combine(Charts, "shared.bbchart");
        ChartPackage.Pack(library.Entries[0], package);

        (var first, _) = ChartPackage.Unpack(package, Cache);
        File.WriteAllText(Path.Combine(first!, "marker"), "still here");
        (var second, _) = ChartPackage.Unpack(package, Cache);

        Assert.Equal(first, second);
        Assert.True(File.Exists(Path.Combine(second!, "marker")));
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
              // kept: comments survive packing
              "name": "Folder Song",
              "music": { "file": "song.wav" },
              "cover": "cover.png",
              "format": 2,
              "tracks": [ { "clips": [ { "t": 1, "clip": "wave" } ] } ]
            }
            """);
        File.WriteAllBytes(Path.Combine(folder, "song.wav"), TestAudio.Write(TestAudio.Metronome(120, 0.5)));
        File.WriteAllBytes(Path.Combine(folder, "cover.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        File.WriteAllText(Path.Combine(folder, ChartPackage.ClipsFolder, "wave.json"), ClipFile.Write(SyntheticClips.Sway("wave", 1f)));
        return folder;
    }

    private static void Write(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(text);
    }
}

public sealed class BestScoreTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"BossBattles-{Guid.NewGuid():N}", "scores.json");

    [Fact]
    public void KeepsOnlyTheBestAndSurvivesARestart()
    {
        var book = new JsonBook<BestScore>(_file);
        book.Load();
        var best = BestScore.From(Result(900, "A"), new DateTime(2026, 1, 1));
        book.Set("Song|me", best);

        Assert.False(best.IsBeatenBy(Result(500, "C")));
        Assert.True(best.IsBeatenBy(Result(1000, "S")));

        var again = new JsonBook<BestScore>(_file);
        again.Load();
        Assert.Equal(best, again.Get("Song|me"));
        Assert.Null(again.Get("Other|me"));
    }

    [Fact]
    public void AnUnreadableFileStartsAFreshBook()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, "{ not json");

        var book = new JsonBook<BestScore>(_file);
        book.Load();

        Assert.NotNull(book.LoadError);
        Assert.Null(book.Get("Song|me"));
    }

    public void Dispose()
    {
        var folder = Path.GetDirectoryName(_file);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private static BattleResult Result(int score, string rank) => new("Song", true, 30, 0, 0, 0, 10, score, rank);
}

public class WavFileTests
{
    [Fact]
    public void WritesAndReadsBack()
    {
        var tone = TestAudio.Metronome(120, 1.0, 8000);

        var read = Decode(TestAudio.Write(tone));

        Assert.Equal(1, read.Channels);
        Assert.Equal(8000, read.SampleRate);
        Assert.Equal(tone.Samples.Length, read.Samples.Length);
        Assert.Equal(1.0, read.Seconds, 3);
        for (var i = 0; i < tone.Samples.Length; i += 97) Assert.Equal(tone.Samples[i], read.Samples[i], 3);
    }

    [Fact]
    public void RefusesWhatIsNotAWav() => Assert.Throws<InvalidDataException>(() => Decode("OggS not a wav at all"u8.ToArray()));

    private static PcmAudio Decode(byte[] wav)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bossbattles-{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(path, wav);
        try
        {
            using var source = AudioDecoder.Open(path);
            return TestAudio.DecodeAll(source);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public sealed class JsonBookTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"BossBattles-{Guid.NewGuid():N}", "places.json");

    [Fact]
    public void ASpotSurvivesARestartAndABrokenFileStartsEmpty()
    {
        var book = new JsonBook<SavedSpot>(_file);
        book.Load();
        Assert.Null(book.Get("Gym"));

        book.Set("Gym", new SavedSpot(1f, 2f, 3f, 90f));
        var again = new JsonBook<SavedSpot>(_file);
        again.Load();
        Assert.Equal(new SavedSpot(1f, 2f, 3f, 90f), again.Get("Gym"));

        File.WriteAllText(_file, "{ broken");
        again.Load();
        Assert.Null(again.Get("Gym"));
    }

    public void Dispose()
    {
        var folder = Path.GetDirectoryName(_file);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }
}

public sealed class ChartAuthoringTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"BossBattles-{Guid.NewGuid():N}");

    [Fact]
    public void NewChartsGetTheirOwnFolderAndAreListed()
    {
        Directory.CreateDirectory(_folder);
        var first = ChartTemplate.Create(_folder, "My Chart", "me");
        var second = ChartTemplate.Create(_folder, "My Chart", "me");

        Assert.NotEqual(first, second);
        var library = new ChartLibrary(_folder, Path.Combine(_folder, "..", "cache-" + Guid.NewGuid().ToString("N")));
        Assert.Equal(0, library.Reload());
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
