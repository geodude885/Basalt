using System.Text.Json.Nodes;
using Basalt.Services.Clips;

namespace Basalt.Services.Charts;

// Keeping a recorded clip, as edits to the chart's text: the actor's track takes it, and the end is pushed out to
// cover it.
public static class TrackWriter
{
    // A chart with a recorded track keeps going this long after the clip ends.
    public const double EndPadding = 1.0;

    public static string KeepWhole(string chartText, Chart chart, string actorId, Clip clip, double at, bool mime = false) =>
        WriteTracks(chartText, chart, TracksWithActorTrackReplaced(chart, actorId, WholeClipTrack(clip, at, mime)), at + clip.Duration - clip.Start);

    public static string KeepSection(string chartText, Chart chart, string actorId, Clip clip, double from, double to, bool mime = false) =>
        WriteTracks(chartText, chart, TracksWithActorTrackReplaced(chart, actorId, PunchedInTrack(chart, actorId, clip.Name, from, to, mime)), to);

    public static IReadOnlyList<TrackClip> WholeClipTrack(Clip clip, double startsAtChartTime, bool mime) =>
        [new TrackClip(Round(startsAtChartTime), clip.Name, Round(clip.Start)) { Mime = mime }];

    public static IReadOnlyList<TrackClip> PunchedInTrack(Chart chart, string actorId, string clipName, double from, double to, bool mime) =>
        PunchIn(chart.Tracks.FirstOrDefault(t => t.ActorId == actorId)?.Clips ?? [], clipName, from, to, mime);

    public static List<Track> TracksWithActorTrackReplaced(Chart chart, string actorId, IReadOnlyList<TrackClip> actorClips) =>
        chart.Tracks.Where(t => t.ActorId != actorId).Append(new Track(actorId, actorClips)).ToList();

    // The actor's clips with chart time `from` to `to` replaced by the clip `name`, played from its start.
    public static IReadOnlyList<TrackClip> PunchIn(IReadOnlyList<TrackClip> clips, string name, double from, double to, bool mime = false)
    {
        var track = new List<TrackClip>();

        // Before the section: each clip as it was, except that one still playing at `from` is cut off there.
        var nextClipStarts = clips.Skip(1).Select(c => (double?)c.Time).Append(null);
        foreach ((var clip, var nextClipStart) in clips.Zip(nextClipStarts).TakeWhile(pair => pair.First.Time < from))
        {
            var stillPlayingAtFrom = nextClipStart == null || nextClipStart > from;
            var cutOffAt = Math.Min(clip.To ?? double.MaxValue, clip.From + (from - clip.Time));
            track.Add(stillPlayingAtFrom ? clip with { To = Round(cutOffAt) } : clip);
        }

        // The section itself.
        track.Add(new TrackClip(Round(from), name, 0, Round(to - from)) { Mime = mime });

        // After it: a clip that would still have been playing at `to` picks up where it would have been, then the
        // clips that start from `to` on, as they were.
        var lastStartedBy = clips.LastOrDefault(c => c.Time <= to);
        if (lastStartedBy != null && lastStartedBy.Time < to)
        {
            var resumeFrom = lastStartedBy.From + (to - lastStartedBy.Time);
            if (lastStartedBy.To == null || resumeFrom < lastStartedBy.To) track.Add(lastStartedBy with { Time = Round(to), From = Round(resumeFrom) });
        }

        track.AddRange(clips.Where(c => c.Time >= to));
        return track;
    }

    // The tracks written, the chart's format raised to one that has tracks, and its end moved to cover the last clip.
    private static string WriteTracks(string chartText, Chart chart, IReadOnlyList<Track> tracks, double lastClipEnd)
    {
        var chartJson = ChartJson.Parse(chartText);
        chartJson["tracks"] = ChartJson.ToNode(tracks.Select(track => new TrackDto
        {
            Actor = track.ActorId,
            Clips = track.Clips.Select(c => new TrackClipDto { T = c.Time, Clip = c.Clip, From = c.From, To = c.To, Mime = c.Mime ? true : null }).ToList(),
        }).ToList());

        var writtenFormat = chartJson["format"] is JsonValue format && format.TryGetValue<int>(out var number) ? number : 1;
        if (writtenFormat < ChartLoader.TracksFormat) chartJson["format"] = ChartLoader.TracksFormat;

        var end = Math.Max(chart.End, Round(lastClipEnd + EndPadding));
        if (end > chart.End) chartJson["end"] = end;
        return ChartJson.Write(chartJson);
    }

    private static double Round(double seconds) => Math.Round(seconds, 3);
}
