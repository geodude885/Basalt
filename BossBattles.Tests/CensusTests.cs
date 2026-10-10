using BossBattles.Services.Logging;
using Xunit;

namespace BossBattles.Tests;

public sealed class CensusTests
{
    [Fact]
    public void CountsAccumulatePerName()
    {
        var census = new Census();

        census.Count("hit");
        census.Count("hit");
        census.Count("miss");

        Assert.Equal(2, census["hit"]);
        Assert.Equal(1, census["miss"]);
        Assert.Equal(0, census["never"]);
    }

    [Fact]
    public void ReportOrdersByCountThenName()
    {
        var census = new Census();

        census.Count("b");
        census.Count("a");
        census.Count("c");
        census.Count("c");

        Assert.Equal("c=2, a=1, b=1", census.Report());
    }

    [Fact]
    public void EmptyReportSaysSo() => Assert.Equal("(nothing counted)", new Census().Report());
}
