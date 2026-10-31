using System.Text.Json.Nodes;
using Basalt.Services.Charts.Model;
using Basalt.Services.Clips;

namespace Basalt.Services.Charts;

// Keeping a recorded clip, as edits to the chart's text: the actor's performance takes it, and the end is pushed out to
// cover it.
public static class PerformanceWriter
{
    // A chart with a recorded performance keeps going this long after the clip ends.
    public const double EndPadding = 1.0;

    public static string KeepWhole(string chartText, Chart chart, string actorId, Clip clip, double at, bool mime = false) =>
        WritePerformances(chartText, chart, PerformancesWithActorsReplaced(chart, actorId, WholeClipPerformance(clip, at, mime)), at + clip.Duration - clip.Start);

    public static IReadOnlyList<PerformanceClip> WholeClipPerformance(Clip clip, double startsAtChartTime, bool mime) =>
        [new PerformanceClip(Round(startsAtChartTime), clip.Name, Round(clip.Start)) { Mime = mime }];

    public static List<ActorPerformance> PerformancesWithActorsReplaced(Chart chart, string actorId, IReadOnlyList<PerformanceClip> actorClips) =>
        chart.Performances.Where(t => t.ActorId != actorId).Append(new ActorPerformance(actorId, actorClips)).ToList();

    // The performances written, the chart's format raised to one that has performances, and its end moved to cover the last clip.
    private static string WritePerformances(string chartText, Chart chart, IReadOnlyList<ActorPerformance> performances, double lastClipEnd)
    {
        var chartJson = ChartJson.Parse(chartText);
        chartJson["performances"] = ChartJson.ToNode(performances.Select(performance => new PerformanceDto
        {
            Actor = performance.ActorId,
            Clips = performance.Clips.Select(c => new PerformanceClipDto { T = c.Time, Clip = c.Clip, Start = c.Start, End = c.End, Mime = c.Mime ? true : null }).ToList(),
        }).ToList());

        var writtenFormat = chartJson["format"] is JsonValue format && format.TryGetValue<int>(out var number) ? number : 1;
        if (writtenFormat < ChartLoader.PerformancesFormat) chartJson["format"] = ChartLoader.PerformancesFormat;

        var end = Math.Max(chart.End, Round(lastClipEnd + EndPadding));
        if (end > chart.End) chartJson["end"] = end;
        return ChartJson.Write(chartJson);
    }

    private static double Round(double seconds) => Math.Round(seconds, 3);
}
