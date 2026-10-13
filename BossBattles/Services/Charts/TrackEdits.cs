using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BossBattles.Services.Clips;

namespace BossBattles.Services.Charts;

/// <summary>
/// Keeping a recorded clip, as text edits to the chart: the actor's track takes it, and the end is
/// pushed out to cover it. Clips are named <c>&lt;actor&gt;-clip-&lt;n&gt;</c> in the chart's <c>clips/</c>.
/// </summary>
public static class TrackEdits
{
    /// <summary>A chart with a recorded track keeps going this long after the clip ends.</summary>
    public const double EndPadding = 1.0;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The next unused clip name for <paramref name="actorId"/> in a clips folder.</summary>
    public static string NextName(string clipsFolder, string actorId)
    {
        var prefix = $"{ChartPackage.FileStem(actorId)}-clip-";
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

    /// <summary>The chart text with <paramref name="clip"/>, playing from chart time <paramref name="at"/>, as the whole of <paramref name="actorId"/>'s track.</summary>
    public static string KeepWhole(string chartText, Chart chart, string actorId, Clip clip, double at, bool mime = false)
    {
        var tracks = chart.Tracks.Where(t => t.ActorId != actorId).ToList();
        tracks.Add(new Track(actorId, new[] { new TrackClip(Round(at), clip.Name, Round(clip.Start)) { Mime = mime } }));
        return WriteTracks(chartText, chart, tracks, at + clip.Duration - clip.Start);
    }

    /// <summary>
    /// The actor's clips with chart time <paramref name="from"/> to <paramref name="to"/> replaced by <paramref name="name"/>,
    /// played from its start; whatever was playing at <paramref name="to"/> resumes from the right point.
    /// </summary>
    public static IReadOnlyList<TrackClip> PunchIn(IReadOnlyList<TrackClip> clips, string name, double from, double to, bool mime = false)
    {
        var result = new List<TrackClip>();

        IEnumerable<double?> nextStarts = clips.Skip(1).Select(t => (double?)t.Time).Append(null);
        foreach ((TrackClip segment, var next) in clips.Zip(nextStarts).TakeWhile(pair => pair.First.Time < from))
        {
            // Cut off at the punch-in if it would play past it.
            var length = from - segment.Time;
            var end = segment.To is { } t ? Math.Min(t, segment.From + length) : segment.From + length;
            var runsPast = next == null || next > from;
            result.Add(runsPast ? segment with { To = Round(end) } : segment);
        }

        result.Add(new TrackClip(Round(from), name, 0, Round(to - from)) { Mime = mime });

        // What was playing at the punch-out picks up where it would have been.
        TrackClip? covering = clips.LastOrDefault(s => s.Time <= to);
        if (covering != null && covering.Time < to && (covering.To == null || covering.From + (to - covering.Time) < covering.To))
            result.Add(covering with { Time = Round(to), From = Round(covering.From + (to - covering.Time)) });

        result.AddRange(clips.Where(s => s.Time >= to));
        return result;
    }

    /// <summary>The chart text after punching <paramref name="clip"/> into <paramref name="actorId"/>'s track from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static string KeepSection(string chartText, Chart chart, string actorId, Clip clip, double from, double to, bool mime = false)
    {
        IReadOnlyList<TrackClip> before = chart.Tracks.FirstOrDefault(t => t.ActorId == actorId)?.Clips ?? Array.Empty<TrackClip>();
        var tracks = chart.Tracks.Where(t => t.ActorId != actorId).ToList();
        tracks.Add(new Track(actorId, PunchIn(before, clip.Name, from, to, mime)));
        return WriteTracks(chartText, chart, tracks, to);
    }

    /// <summary>The chart text with <paramref name="tracks"/> written, the format raised and the end covering <paramref name="lastClipEnd"/>.</summary>
    public static string WriteTracks(string chartText, Chart chart, IReadOnlyList<Track> tracks, double lastClipEnd)
    {
        var text = ChartDocument.SetRaw(chartText, "tracks", JsonSerializer.Serialize(tracks.Select(track => new TrackDto
        {
            Actor = track.ActorId,
            Clips = track.Clips.Select(t => new TrackClipDto { T = t.Time, Clip = t.Clip, From = t.From, To = t.To, Mime = t.Mime ? true : null }).ToList(),
        }).ToList(), Options));

        if (ChartLoader.TracksFormat > CurrentFormat(chartText)) text = ChartDocument.SetRaw(text, "format", ChartLoader.TracksFormat.ToString(CultureInfo.InvariantCulture));

        var end = Math.Max(chart.End, Round(lastClipEnd + EndPadding));
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

    private static double Round(double seconds) => Math.Round(seconds, 3);

    private sealed class TrackDto
    {
        public string Actor { get; set; } = "";
        public List<TrackClipDto> Clips { get; set; } = [];
    }

    private sealed class TrackClipDto
    {
        public double T { get; set; }
        public string Clip { get; set; } = "";
        public double From { get; set; }
        public double? To { get; set; }
        public bool? Mime { get; set; }
    }
}
