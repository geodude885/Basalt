using System.Globalization;
using System.Numerics;
using System.Text.Json;
using static Basalt.Services.Charts.ChartLoader;

namespace Basalt.Services.Charts;

/// <summary>The outcome of loading a chart: a chart, or every problem found, each with its path in the file.</summary>
public record ChartLoadResult(Chart? Chart, IReadOnlyList<string> Errors)
{
    public bool Ok => Chart != null;
}

// Reads and validates a chart; ChartReader does the reading.
public static class ChartLoader
{
    public const float DefaultLaneLength = 12f;
    public const float DefaultLaneWidth = 3f;
    public const float DefaultTelegraph = 0.5f;

    /// <summary>How far outside the lane - past its far end or its sides - a projectile may start.</summary>
    public const float ProjectileReach = 6f;
    public const string DefaultActorId = "actor";
    public const string DefaultLook = "you";

    /// <summary>The newest chart format this build reads. A chart may say which it was written for with "format".</summary>
    public const int NewestFormat = 2;

    /// <summary>Recorded tracks arrived in format 2: a chart using them says so, so an older mod refuses it rather than playing it without them.</summary>
    public const int TracksFormat = 2;

    /// <summary>Song files a chart may name; Unity decodes these.</summary>
    public static readonly IReadOnlyList<string> MusicExtensions = new[] { ".ogg", ".wav", ".mp3" };

    /// <summary>Cover images a chart may name; Unity decodes these.</summary>
    public static readonly IReadOnlyList<string> CoverExtensions = new[] { ".png", ".jpg", ".jpeg" };

    /// <summary>With no "end", the battle ends this long after its last event.</summary>
    public const double DefaultEndPadding = 3.0;

    private static readonly string[] EmptyFile = { "the file is empty" };

    public static ChartLoadResult Load(string json)
    {
        ChartDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ChartDto>(json, ChartJson.Options);
        }
        catch (JsonException e)
        {
            return new ChartLoadResult(null, new[] { $"not valid chart JSON: {e.Message}" });
        }

        if (dto == null) return new ChartLoadResult(null, EmptyFile);

        if (dto.Format is > NewestFormat)
            return new ChartLoadResult(null, new[] { $"made for a newer Basalt (chart format {dto.Format}; this one reads up to {NewestFormat}) - update the mod" });

        var reader = new ChartReader(dto);
        var chart = reader.Build();
        return reader.Errors.Count == 0 ? new ChartLoadResult(chart, reader.Errors) : new ChartLoadResult(null, reader.Errors);
    }
}

// Builds a chart from its parsed JSON, collecting every problem - each with its path in the file - rather than
// stopping at the first.
internal class ChartReader(ChartDto dto)
{
    private Lane _lane;
    private TempoMap? _tempo;
    private HashSet<string> _actorIds = [];
    private string _defaultActorId = DefaultActorId;

    public List<string> Errors { get; } = [];

    public Chart Build()
    {
        _lane = new Lane(dto.Lane?.Length ?? DefaultLaneLength, dto.Lane?.Width ?? DefaultLaneWidth);
        if (_lane.Length <= 2f) Errors.Add($"lane.length must be over 2 m (got {Format(_lane.Length)})");
        if (_lane.Width <= 0f) Errors.Add($"lane.width must be over 0 (got {Format(_lane.Width)})");

        _tempo = ReadTempo(dto.Bpm);

        var actors = Actors();
        _actorIds = actors.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        _defaultActorId = actors[0].Id;

        var phases = Phases();
        var events = Events();

        var lastEvent = events.Count == 0 ? 0 : events.Max(e => e.Time);
        var end = dto.End ?? (lastEvent + DefaultEndPadding);
        if (end < lastEvent) Errors.Add($"end ({Format(end)}) is before the last event ({Format(lastEvent)})");

        return new Chart(string.IsNullOrWhiteSpace(dto.Name) ? "Untitled" : dto.Name, _lane, actors, phases, events, end)
        {
            Info = new ChartInfo(Blank(dto.Author), Blank(dto.Description), Blank(dto.Difficulty)),
            Arena = ReadArena(dto.Arena),
            Music = ReadMusic(dto.Music),
            Cover = ReadCover(dto.Cover),
            Tracks = Tracks(),
            Zone = ReadZone(dto.Zone),
            Rules = ReadRules(dto.Rules),
            Tempo = _tempo,
            Visuals = ReadVisuals(dto.Visuals),
            Colours = ReadColours(dto.Colours ?? dto.Colors, "colours") ?? LaneColours.Default,
        };
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private ArenaSpec ReadArena(ArenaDto? arena)
    {
        if (arena == null) return ArenaSpec.Here;

        var map = ArenaKind.Here;
        if (arena.Map != null && !Enum.TryParse(arena.Map, ignoreCase: true, out map))
        {
            Errors.Add($"arena.map \"{arena.Map}\" is not one of {string.Join(", ", Enum.GetNames<ArenaKind>().Select(n => n.ToLowerInvariant()))}");
            return ArenaSpec.Here;
        }

        if (arena.At != null && (arena.At.X == null || arena.At.Z == null))
        {
            Errors.Add("arena.at needs x and z");
            return new ArenaSpec(map);
        }

        if (map == ArenaKind.Here && (arena.At != null || arena.Facing != null))
            Errors.Add("arena.at and arena.facing need a map - \"here\" is wherever you stand");

        return new ArenaSpec(map, arena.At?.X, arena.At?.Z, arena.Facing);
    }

    private Zone ReadZone(ZoneDto? zone)
    {
        var defaults = Zone.For(_lane);
        if (zone == null) return defaults;

        var length = zone.Length ?? defaults.Length;
        var height = zone.Height ?? defaults.Height;
        if (length <= 0f || length > _lane.Length) Errors.Add($"zone.length ({Format(length)}) must be over 0 and up to the lane's length ({Format(_lane.Length)})");
        if (height < 1f) Errors.Add($"zone.height ({Format(height)}) must be at least 1 m - room to stand");
        return new Zone(length, height);
    }

    private LaneColours? ReadColours(ColoursDto? colours, string path)
    {
        if (colours == null) return null;

        Rgba? Read(string? text, string name)
        {
            if (text == null) return null;
            if (Rgba.TryParse(text, out var colour)) return colour;
            Errors.Add($"{path}.{name} \"{text}\" is not a colour - use \"#RRGGBB\", \"#RRGGBBAA\" with opacity, or \"none\"");
            return null;
        }

        return new LaneColours(Read(colours.Edges, "edges"), Read(colours.Ends, "ends"), Read(colours.Zone, "zone"), Read(colours.Backstop, "backstop"), Read(colours.Walls, "walls"));
    }

    private List<VisualCue> ReadVisuals(List<VisualDto>? visuals)
    {
        var cues = new List<VisualCue>();
        if (visuals == null) return cues;

        foreach ((var v, var i) in visuals.Select((v, i) => (v, i)))
        {
            var path = $"visuals[{i}]";
            var before = Errors.Count;

            var time = Time(v.T, v.Beat, path);
            if (string.IsNullOrWhiteSpace(v.Pack)) Errors.Add($"{path} needs a pack - a RumbleReShaded pack's name, e.g. \"UltraShade\"");
            var off = v.Off ?? false;
            if (off && v.Set is { Count: > 0 }) Errors.Add($"{path} has both \"off\" and \"set\" - an off fades the pack back to the player's own settings");
            if (!off && v.Set is not { Count: > 0 } && v.On != true) Errors.Add($"{path} needs \"set\" (parameter values), \"on\": true or \"off\": true");
            var fade = v.Fade ?? 0;
            if (fade < 0) Errors.Add($"{path}.fade cannot be negative (got {Format(fade)})");

            if (Errors.Count == before)
                cues.Add(new VisualCue(time!.Value, v.Pack!.Trim(), new Dictionary<string, float>(v.Set ?? [], StringComparer.OrdinalIgnoreCase), off, fade));
        }

        return cues.OrderBy(c => c.Time).ToList();
    }

    private Rules ReadRules(RulesDto? rules)
    {
        if (rules == null) return Rules.Default;

        var spawn = SpawnRule.All;
        if (rules.Spawn != null && !Enum.TryParse(rules.Spawn, ignoreCase: true, out spawn))
        {
            Errors.Add($"rules.spawn \"{rules.Spawn}\" is not one of all, discs, none");
            spawn = SpawnRule.All;
        }

        var cleanup = rules.Cleanup ?? Rules.DefaultCleanupSeconds;
        if (cleanup < 0) Errors.Add($"rules.cleanup cannot be negative (got {Format(cleanup)}) - 0 turns it off");
        return new Rules(spawn) { CleanupSeconds = Math.Max(0, cleanup) };
    }

    private List<Track> Tracks()
    {
        var tracks = new List<Track>();
        if (dto.Tracks == null || dto.Tracks.Count == 0) return tracks;

        if ((dto.Format ?? 1) < TracksFormat)
            Errors.Add($"tracks need \"format\": {TracksFormat} or later - set it at the top of the chart");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach ((var track, var i) in dto.Tracks.Select((t, i) => (t, i)))
        {
            var path = $"tracks[{i}]";
            var actor = track.Actor == null ? _defaultActorId : ActorRef(track.Actor, $"{path}.actor");
            if (!seen.Add(actor)) Errors.Add($"{path}: \"{actor}\" already has a track - one per actor");

            var clips = new List<TrackClip>();
            foreach ((var clip, var j) in (track.Clips ?? []).Select((t, j) => (t, j)))
            {
                var clipPath = $"{path}.clips[{j}]";

                var time = Time(clip.T, clip.Beat, clipPath);
                if (string.IsNullOrWhiteSpace(clip.Clip)) Errors.Add($"{clipPath}.clip needs the clip's name (a file in clips/)");
                var from = clip.From ?? 0;
                if (from < 0) Errors.Add($"{clipPath}.from cannot be negative (got {Format(from)})");
                if (clip.To is { } to && to <= from) Errors.Add($"{clipPath}.to ({Format(to)}) must be after from ({Format(from)})");
                if (time == null || string.IsNullOrWhiteSpace(clip.Clip)) continue;

                if (clips.Count > 0 && time <= clips[^1].Time)
                    Errors.Add($"{clipPath}.t ({Format(time.Value)}) must be after the clip before it ({Format(clips[^1].Time)})");
                clips.Add(new TrackClip(time.Value, clip.Clip, from, clip.To) { Mime = clip.Mime ?? false });
            }

            tracks.Add(new Track(actor, clips));
        }

        return tracks;
    }

    private string? ReadCover(string? cover)
    {
        if (cover == null) return null;

        var file = cover.Trim();
        if (!ChartFiles.IsSafeRelative(file))
        {
            Errors.Add($"cover \"{cover}\" must be a path inside the chart's folder or package");
            return null;
        }

        if (!CoverExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
        {
            Errors.Add($"cover \"{cover}\" must be one of {string.Join(", ", CoverExtensions)}");
            return null;
        }

        return file;
    }

    private MusicSpec? ReadMusic(MusicDto? music)
    {
        if (music == null) return null;

        var before = Errors.Count;
        var file = music.File?.Trim() ?? "";
        if (file.Length == 0) Errors.Add("music.file is required - a song next to the chart, e.g. \"song.ogg\"");
        else if (!ChartFiles.IsSafeRelative(file)) Errors.Add($"music.file \"{file}\" must be a path inside the chart's folder or package");
        else if (!MusicExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
            Errors.Add($"music.file \"{file}\" must be one of {string.Join(", ", MusicExtensions)}");

        var start = music.Start ?? 0;

        var volume = music.Volume ?? 1f;
        if (volume is < 0f or > 1f) Errors.Add($"music.volume must be 0 to 1 (got {Format(volume)})");

        return Errors.Count == before ? new MusicSpec(file, start, volume) : null;
    }

    /// <summary><c>"bpm": 120</c>, or tempo changes: <c>"bpm": [ { "beat": 0, "bpm": 120 }, { "beat": 64, "bpm": 140 } ]</c>.</summary>
    private TempoMap? ReadTempo(JsonElement? bpm)
    {
        if (bpm is not { } value || value.ValueKind == JsonValueKind.Null) return null;

        if (value.ValueKind == JsonValueKind.Number)
        {
            var single = value.GetDouble();
            if (single > 0) return TempoMap.Constant(single);
            Errors.Add($"bpm must be over 0 (got {Format(single)})");
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            Errors.Add("bpm must be a number, or a list of tempo changes: [ { \"beat\": 0, \"bpm\": 120 }, ... ]");
            return null;
        }

        var changes = new List<TempoChange>();
        var index = 0;
        foreach (var change in value.EnumerateArray())
        {
            var path = $"bpm[{index++}]";
            if (change.ValueKind != JsonValueKind.Object
                || !TryNumber(change, "beat", out var beat) || !TryNumber(change, "bpm", out var rate))
            {
                Errors.Add($"{path} needs a beat and a bpm");
                continue;
            }

            if (rate <= 0) Errors.Add($"{path}.bpm must be over 0 (got {Format(rate)})");
            else if (changes.Count == 0 && beat != 0) Errors.Add($"{path}.beat must be 0 - the first tempo starts the song");
            else if (changes.Count > 0 && beat <= changes[^1].Beat) Errors.Add($"{path}.beat ({Format(beat)}) must be after the change before it ({Format(changes[^1].Beat)})");
            else changes.Add(new TempoChange(beat, rate));
        }

        if (changes.Count == 0)
        {
            if (index == 0) Errors.Add("bpm has no tempo changes");
            return null;
        }

        return new TempoMap(changes);
    }

    private static bool TryNumber(JsonElement element, string name, out double number)
    {
        var found = element.EnumerateObject()
            .Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Number)
            .Select(p => (double?)p.Value.GetDouble())
            .FirstOrDefault();
        number = found ?? 0;
        return found != null;
    }

    private List<ActorSpec> Actors()
    {
        if (dto.Actors == null || dto.Actors.Count == 0)
            return [new(DefaultActorId, "Actor", new Spot(0f, _lane.Length), DefaultLook)];

        var actors = new List<ActorSpec>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach ((var actor, var i) in dto.Actors.Select((a, i) => (a, i)))
        {
            var path = $"actors[{i}]";

            var id = string.IsNullOrWhiteSpace(actor.Id) ? (i == 0 ? DefaultActorId : $"actor{i + 1}") : actor.Id;
            if (!seen.Add(id)) Errors.Add($"{path}.id \"{id}\" is used twice");

            var at = actor.At == null ? new Spot(0f, _lane.Length) : ReadSpot(actor.At, $"{path}.at");
            var look = string.IsNullOrWhiteSpace(actor.Look) ? DefaultLook : actor.Look;

            actors.Add(new ActorSpec(id, string.IsNullOrWhiteSpace(actor.Name) ? id : actor.Name, at, look)
            {
                Shiftstones = ReadStones(actor.Shiftstones, $"{path}.shiftstones"),
                Measurement = ReadMeasurement(actor.Measurement, $"{path}.measurement"),
            });
        }

        return actors;
    }

    private BodySize? ReadMeasurement(MeasurementDto? measurement, string path)
    {
        if (measurement == null) return null;

        if (measurement.Height is not (>= 0.5f and <= 3f) || measurement.ArmSpan is not (>= 0.5f and <= 3f))
        {
            Errors.Add($"{path} needs a height and armSpan in metres, from 0.5 to 3");
            return null;
        }

        return new BodySize(measurement.Height.Value, measurement.ArmSpan.Value);
    }

    private List<string>? ReadStones(JsonElement? stones, string path)
    {
        if (stones is not { } value || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), "you", StringComparison.OrdinalIgnoreCase)) return null;

        if (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(s => s.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(s.GetString())))
        {
            Errors.Add($"{path} must be \"you\" or a list of stone names, e.g. [\"Vigor\", \"Flow\"]");
            return null;
        }

        var names = value.EnumerateArray().Select(s => s.GetString()!.Trim()).ToList();
        if (names.Count > 2) Errors.Add($"{path} has {names.Count} stones - a player wears two at most");
        return names;
    }

    private List<Phase> Phases()
    {
        if (dto.Phases == null || dto.Phases.Count == 0) return [new("Battle", 0, 1, null, null)];

        var phases = new List<Phase>();
        foreach ((var phase, var i) in dto.Phases.Select((p, i) => (p, i)))
        {
            var path = $"phases[{i}]";

            var start = Time(phase.Start, phase.StartBeat, path, "start", "startBeat") ?? 0;
            if (i == 0 && start != 0) Errors.Add($"{path} must start at 0 - the first phase starts the battle");
            if (i > 0 && start <= phases[^1].Start)
                Errors.Add($"{path}.start ({Format(start)}) must be after phases[{i - 1}] ({Format(phases[^1].Start)})");

            var multiplier = phase.ScoreMultiplier ?? 1;
            if (multiplier <= 0) Errors.Add($"{path}.scoreMultiplier must be over 0 (got {Format(multiplier)})");

            var actor = phase.Actor == null ? null : ActorRef(phase.Actor, $"{path}.actor");
            Spot? at = phase.At == null ? null : ReadSpot(phase.At, $"{path}.at");

            phases.Add(new Phase(string.IsNullOrWhiteSpace(phase.Name) ? $"Phase {i + 1}" : phase.Name, start, multiplier, actor, at)
            {
                Colours = ReadColours(phase.Colours ?? phase.Colors, $"{path}.colours"),
            });
        }

        return phases;
    }

    private List<ChartEvent> Events()
    {
        var events = new List<ChartEvent>();
        if (dto.Events == null) return events;

        foreach ((var e, var i) in dto.Events.Select((e, i) => (e, i)))
        {
            var path = $"events[{i}]";

            var time = Time(e.T, e.Beat, path);
            if (time == null) continue;

            var kinds = (e.Projectile != null ? 1 : 0) + (e.Move != null ? 1 : 0) + (e.At != null ? 1 : 0);
            if (kinds != 1)
            {
                Errors.Add($"{path} needs exactly one of projectile, move or at (has {kinds})");
                continue;
            }

            var actor = e.Actor == null ? _defaultActorId : ActorRef(e.Actor, $"{path}.actor");

            if (e.Projectile != null)
            {
                var projectile = Projectile(e.Projectile, time.Value, i, $"{path}.projectile");
                if (projectile != null) events.Add(projectile);
            }
            else if (e.Move != null)
            {
                if (string.IsNullOrWhiteSpace(e.Move)) Errors.Add($"{path}.move needs a move name, e.g. \"Straight\"");
                else events.Add(new MoveEvent(time.Value, i, actor, e.Move));
            }
            else
            {
                events.Add(new PlaceEvent(time.Value, i, actor, ReadSpot(e.At!, $"{path}.at")));
            }
        }

        return events;
    }

    private ProjectileEvent? Projectile(ProjectileDto p, double time, int order, string path)
    {
        var before = Errors.Count;

        if (string.IsNullOrWhiteSpace(p.Structure)) Errors.Add($"{path}.structure is required, e.g. \"Disc\"");

        var from = Vector3.Zero;
        if (p.From?.X == null || p.From.Y == null || p.From.Z == null)
        {
            Errors.Add($"{path}.from needs x, y and z");
        }
        else
        {
            from = new Vector3(p.From.X.Value, p.From.Y.Value, p.From.Z.Value);

            if (from.Z <= 0f || from.Z > _lane.Length + ProjectileReach)
                Errors.Add($"{path}.from.z ({Format(from.Z)}) must be in front of you and within the lane or up to {Format(ProjectileReach)} m past it: over 0, up to {Format(_lane.Length + ProjectileReach)}");
            var side = (_lane.Width / 2f) + ProjectileReach;
            if (Math.Abs(from.X) > side)
                Errors.Add($"{path}.from.x ({Format(from.X)}) must be within {Format(ProjectileReach)} m of the lane's sides: -{Format(side)} to {Format(side)}");
            if (from.Y < 0f) Errors.Add($"{path}.from.y ({Format(from.Y)}) is below the floor");
        }

        var aim = Aim.Player;
        if (p.To is JsonElement to)
        {
            if (to.ValueKind == JsonValueKind.String && string.Equals(to.GetString(), "player", StringComparison.OrdinalIgnoreCase))
            {
                aim = Aim.Player;
            }
            else if (to.ValueKind == JsonValueKind.Object
                     && to.TryGetProperty("x", out var x) && x.TryGetSingle(out var tx)
                     && to.TryGetProperty("y", out var y) && y.TryGetSingle(out var ty))
            {
                aim = Aim.Point(tx, ty);
            }
            else
            {
                Errors.Add($"{path}.to must be \"player\" or {{ \"x\": ..., \"y\": ... }}");
            }
        }

        var speed = p.Speed ?? 0f;
        if (speed <= 0f) Errors.Add($"{path}.speed must be over 0 m/s (got {Format(speed)})");

        var telegraph = p.Telegraph ?? DefaultTelegraph;
        if (telegraph < 0f) Errors.Add($"{path}.telegraph cannot be negative (got {Format(telegraph)})");
        else if (time - telegraph < 0)
            Errors.Add($"{path} would appear before the battle starts: launch at {Format(time)} with a {Format(telegraph)} s telegraph");

        var by = p.By == null ? null : ActorRef(p.By, $"{path}.by");

        return Errors.Count == before ? new ProjectileEvent(time, order, p.Structure!, from, aim, speed, telegraph, by) { Grounded = p.Grounded } : null;
    }

    private string ActorRef(string id, string path)
    {
        if (!_actorIds.Contains(id))
            Errors.Add($"{path} \"{id}\" is not one of the chart's actors ({string.Join(", ", _actorIds)})");
        return id;
    }

    private Spot ReadSpot(PointDto point, string path)
    {
        if (point.X == null || point.Z == null)
        {
            Errors.Add($"{path} needs x and z");
            return default;
        }

        if (point.Z < 0f || point.Z > _lane.Length)
            Errors.Add($"{path}.z ({Format(point.Z.Value)}) must be within the lane: 0 to {Format(_lane.Length)}");

        return new Spot(point.X.Value, point.Z.Value, point.Facing ?? 0f);
    }

    /// <summary>Seconds from either a seconds field or a beats field - exactly one of them.</summary>
    private double? Time(double? seconds, double? beats, string path, string secondsName = "t", string beatsName = "beat")
    {
        if (seconds != null && beats != null)
        {
            Errors.Add($"{path} has both {secondsName} and {beatsName} - use one");
            return null;
        }

        if (beats != null && _tempo == null)
        {
            Errors.Add($"{path}.{beatsName} needs the chart's bpm");
            return null;
        }

        if (beats != null) seconds = _tempo!.Seconds(beats.Value);

        if (seconds == null)
        {
            Errors.Add($"{path} needs a time: {secondsName} (seconds) or {beatsName}");
            return null;
        }

        if (seconds < 0)
        {
            Errors.Add($"{path}.{secondsName} cannot be negative (got {Format(seconds.Value)})");
            return null;
        }

        return seconds;
    }

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
