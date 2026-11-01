using Basalt.Services.Charts.Files;
using Basalt.Services.Charts.Model;
using Xunit;

namespace Basalt.Tests.Services.Charts.Files;

public class ChartWritingTests
{
    private const string HandWritten = /*lang=json*/ """
        {
          // a comment
          "name": "Song", /* another */
          "description": "Don't panic - it's été",
          "lane": { "length": 12, "width": 3, },
          "actors": [ { "id": "boss" } ],
        }
        """;

    [Fact]
    public void CommentsAndTrailingCommasAreReadAndPlainJsonIsWritten()
    {
        var saved = ChartWriter.WriteActors(HandWritten, [new ActorSpec("boss", "Boss", new Spot(0f, 10f), "you")]);
        var chart = ChartLoader.Load(saved).Chart!;

        Assert.Equal("Song", chart.Name);
        Assert.Equal(12f, chart.Lane.Length);
        Assert.Equal(10f, chart.Actors[0].At.Z);
        Assert.DoesNotContain("//", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void ApostrophesAndAccentsAreWrittenAsTheyAre()
    {
        var saved = ChartWriter.WriteActors(HandWritten, []);

        Assert.Contains("Don't panic - it's été", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void KeysAreMatchedAsTheLoaderMatchesThem()
    {
        var saved = ChartWriter.WriteActors(/*lang=json,strict*/ """{ "Actors": [ { "id": "old" } ] }""", [new ActorSpec("new", "New", new Spot(0f, 5f), "you")]);

        Assert.Equal("new", Assert.Single(ChartLoader.Load(saved).Chart!.Actors).Id);
        Assert.DoesNotContain("old", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void NotAnObjectIsRefused() => Assert.Throws<FormatException>(() => ChartWriter.WriteActors("[1]", []));

    [Fact]
    public void BrokenJsonIsRefusedAsAFormatProblem() => Assert.Throws<FormatException>(() => ChartWriter.WriteActors("{ \"name\": ", []));
}
