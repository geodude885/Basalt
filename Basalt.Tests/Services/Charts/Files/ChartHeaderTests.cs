using Basalt.Services.Charts.Files;
using Basalt.Services.Charts.Model;
using Xunit;

namespace Basalt.Tests.Services.Charts.Files;

public class ChartHeaderTests
{
    [Fact]
    public void ReadsInfoArenaAndMusic()
    {
        var chart = Load(/*lang=json,strict*/ """
            {
              "format": 1, "name": "Song", "author": "someone", "difficulty": "Hard", "description": "A song.",
              "arena": { "map": "Ring", "at": { "x": 1.5, "z": -6, "facing": 90 } },
              "music": { "file": "music/song.ogg", "offset": 2.5, "volume": 0.7 }
            }
            """);

        Assert.Equal(new ChartInfo("someone", "A song.", "Hard"), chart.Info);
        Assert.Equal(new ArenaSpec(ArenaKind.Ring, 1.5f, -6f, 90f), chart.Arena);
        Assert.Equal(new MusicSpec("music/song.ogg", 2.5, 0.7f), chart.Music);
    }

    [Fact]
    public void ANegativeMusicStartIsSilenceBeforeTheSong() =>
        Assert.Equal(new MusicSpec("song.ogg", -1.5), Load(/*lang=json,strict*/ """{ "music": { "file": "song.ogg", "offset": -1.5 } }""").Music);

    [Fact]
    public void ReadsACover() => Assert.Equal("art/cover.png", Load(/*lang=json,strict*/ """{ "cover": "art/cover.png" }""").Cover);

    [Fact]
    public void ANewChartIsBlankButForItsHeader()
    {
        var result = ChartLoader.Load(ChartTemplate.Json("Quote \" test", "someone"));

        Assert.True(result.Ok, string.Join("; ", result.Errors));
        Assert.Equal("Quote \" test", result.Chart!.Name);
        Assert.Equal("someone", result.Chart.Info.Author);
        Assert.Empty(result.Chart.Events);
    }

    [Fact]
    public void WithoutAHeaderAChartIsPlayedInTheRingInSilence()
    {
        var chart = Load(/*lang=json,strict*/ """{ "name": "Plain" }""");

        Assert.Equal(new ArenaSpec(ArenaKind.Ring, 0f, -6f, 0f), chart.Arena);
        Assert.Null(chart.Music);
        Assert.Equal(ChartInfo.None, chart.Info);
    }

    [Fact]
    public void AMapWithoutASpotStartsAtItsCentreFacingNorth()
    {
        var arena = Load(/*lang=json,strict*/ """{ "arena": { "map": "pit" } }""").Arena;

        Assert.Equal(new ArenaSpec(ArenaKind.Pit, 0f, 0f, 0f), arena);
    }

    [Theory]
    [InlineData(/*lang=json,strict*/ """{ "format": 99 }""", "newer Basalt")]
    [InlineData(/*lang=json,strict*/ """{ "arena": { "map": "moon" } }""", "arena.map \"moon\"")]
    [InlineData(/*lang=json,strict*/ """{ "arena": { "map": "ring", "at": { "x": 1 } } }""", "arena.at needs x and z")]
    [InlineData(/*lang=json,strict*/ """{ "arena": { "map": "here" } }""", "arena.map \"here\" is not one of gym, ring, pit")]
    [InlineData(/*lang=json,strict*/ """{ "music": { "offset": 1 } }""", "music.file is required")]
    [InlineData(/*lang=json,strict*/ """{ "music": { "file": "../../secret.ogg" } }""", "inside the chart's folder")]
    [InlineData(/*lang=json,strict*/ """{ "music": { "file": "C:/music/song.ogg" } }""", "inside the chart's folder")]
    [InlineData(/*lang=json,strict*/ """{ "music": { "file": "song.flac" } }""", "must be one of")]
    [InlineData(/*lang=json,strict*/ """{ "music": { "file": "song.ogg", "volume": 2 } }""", "music.volume")]
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

    private static Chart Load(string json)
    {
        var result = ChartLoader.Load(json);
        return result.Chart ?? throw new InvalidOperationException(string.Join("; ", result.Errors));
    }
}
