using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BossBattles.Services.Clips;

namespace BossBattles.Services.Charts;

/// <summary>
/// What keeping a recorded take does to a chart, as text edits (<see cref="ChartDocument"/>): the
/// actor's track becomes the take, the chart's format is raised to one that reads tracks, and its
/// end is pushed out to cover the take. Takes are named <c>&lt;actor&gt;-take-&lt;n&gt;</c> and kept
/// in the chart's <c>clips/</c>.
/// </summary>
public static class Takes
{
    /// <summary>A chart with a recorded track keeps going this long after the take ends.</summary>
    public const double EndPadding = 1.0;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The next unused take name for <paramref name="actorId"/> in a clips folder.</summary>
    public static string NextName(string clipsFolder, string actorId)
    {
        var prefix = $"{ChartPackage.FileStem(actorId)}-take-";
        var highest = 0;

        if (Directory.Exists(clipsFolder))
        {
            foreach (var file in Directory.EnumerateFiles(clipsFolder, prefix + "*.json"))
            {
                var number = Path.GetFileNameWithoutExtension(file)[prefix.Length..];
                if (int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) highest = Math.Max(highest, n);
            }
        }

        return $"{prefix}{highest + 1}";
    }

    /// <summary>
    /// The chart text after keeping <paramref name="take"/> as the whole of <paramref name="actorId"/>'s
    /// track. The take keeps chart time, so it plays from its own first frame's time.
    /// </summary>
    public static string KeepWhole(string chartText, Chart chart, string actorId, Clip take, bool mime = false)
    {
        var tracks = chart.Tracks.Where(t => t.ActorId != actorId).ToList();
        tracks.Add(new Track(actorId, new[] { new TakeSegment(Round(take.Start), take.Name, Round(take.Start)) { Mime = mime } }));
        return WriteTracks(chartText, chart, tracks, take.Duration);
    }

    /// <summary>
    /// The actor's takes with chart time <paramref name="from"/> to <paramref name="to"/> replaced
    /// by <paramref name="take"/> (recorded on chart time, so played from <paramref name="from"/>):
    /// what played before is cut off there, and whatever was playing at <paramref name="to"/>
    /// resumes from the right point. Earlier takes are not touched - only which parts play.
    /// </summary>
    public static IReadOnlyList<TakeSegment> PunchIn(IReadOnlyList<TakeSegment> takes, string take, double from, double to, bool mime = false)
    {
        var result = new List<TakeSegment>();

        for (var i = 0; i < takes.Count; i++)
        {
            TakeSegment segment = takes[i];
            if (segment.Time >= from) break;

            // Cut off at the punch-in if it would play past it.
            var length = from - segment.Time;
            double? end = segment.To is { } t ? Math.Min(t, segment.From + length) : segment.From + length;
            var runsPast = i + 1 >= takes.Count || takes[i + 1].Time > from;
            result.Add(runsPast ? segment with { To = Round(end.Value) } : segment);
        }

        result.Add(new TakeSegment(Round(from), take, Round(from), Round(to)) { Mime = mime });

        // What was playing at the punch-out picks up where it would have been.
        TakeSegment? covering = takes.LastOrDefault(s => s.Time <= to);
        if (covering != null && covering.Time < to && (covering.To == null || covering.From + (to - covering.Time) < covering.To))
            result.Add(covering with { Time = Round(to), From = Round(covering.From + (to - covering.Time)) });

        result.AddRange(takes.Where(s => s.Time >= to));
        return result;
    }

    /// <summary>The chart text after punching <paramref name="take"/> into <paramref name="actorId"/>'s track from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static string KeepSection(string chartText, Chart chart, string actorId, Clip take, double from, double to, bool mime = false)
    {
        IReadOnlyList<TakeSegment> before = chart.Tracks.FirstOrDefault(t => t.ActorId == actorId)?.Takes ?? Array.Empty<TakeSegment>();
        var tracks = chart.Tracks.Where(t => t.ActorId != actorId).ToList();
        tracks.Add(new Track(actorId, PunchIn(before, take.Name, from, to, mime)));
        return WriteTracks(chartText, chart, tracks, take.Duration);
    }

    /// <summary>The chart text with <paramref name="tracks"/> written, the format raised and the end covering <paramref name="lastTakeEnd"/>.</summary>
    public static string WriteTracks(string chartText, Chart chart, IReadOnlyList<Track> tracks, double lastTakeEnd)
    {
        var text = ChartDocument.SetRaw(chartText, "tracks", JsonSerializer.Serialize(tracks.Select(ToDto).ToList(), Options));

        if (ChartLoader.TracksFormat > CurrentFormat(chartText)) text = ChartDocument.SetRaw(text, "format", ChartLoader.TracksFormat.ToString(CultureInfo.InvariantCulture));

        var end = Math.Max(chart.End, Round(lastTakeEnd + EndPadding));
        if (end > chart.End) text = ChartDocument.SetRaw(text, "end", end.ToString("0.###", CultureInfo.InvariantCulture));

        return text;
    }

    private static int CurrentFormat(string chartText)
    {
        try
        {
            using var document = JsonDocument.Parse(chartText, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "format", StringComparison.OrdinalIgnoreCase) && property.Value.TryGetInt32(out var format)) return format;
            }
        }
        catch (JsonException)
        {
            // Not readable: treated as format 1, and raised.
        }

        return 1;
    }

    private static TrackDto ToDto(Track track) => new()
    {
        Actor = track.ActorId,
        Takes = track.Takes.Select(t => new TakeDto { T = t.Time, Clip = t.Clip, From = t.From, To = t.To, Mime = t.Mime ? true : null }).ToList(),
    };

    private static double Round(double seconds) => Math.Round(seconds, 3);

    private sealed class TrackDto
    {
        public string Actor { get; set; } = "";
        public List<TakeDto> Takes { get; set; } = [];
    }

    private sealed class TakeDto
    {
        public double T { get; set; }
        public string Clip { get; set; } = "";
        public double From { get; set; }
        public double? To { get; set; }
        public bool? Mime { get; set; }
    }
}
