using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace BossBattles.Services.Charts;

/// <summary>The outcome of loading a chart: a chart, or every problem found, each with its path in the file.</summary>
public record ChartLoadResult(Chart? Chart, IReadOnlyList<string> Errors)
{
    public bool Ok => Chart != null;
}

/// <summary>
/// Reads and validates a chart, collecting every problem rather than stopping at the first.
/// Comments and trailing commas are allowed: charts are hand-written.
/// </summary>
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

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ChartLoadResult Load(string json)
    {
        ChartDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ChartDto>(json, Options);
        }
        catch (JsonException e)
        {
            return new ChartLoadResult(null, new[] { $"not valid chart JSON: {e.Message}" });
        }

        if (dto == null) return new ChartLoadResult(null, EmptyFile);

        if (dto.Format is > NewestFormat)
            return new ChartLoadResult(null, new[] { $"made for a newer BossBattles (chart format {dto.Format}; this one reads up to {NewestFormat}) - update the mod" });

        var errors = new List<string>();
        Chart chart = Build(dto, errors);
        return errors.Count == 0 ? new ChartLoadResult(chart, errors) : new ChartLoadResult(null, errors);
    }

    private static Chart Build(ChartDto dto, List<string> errors)
    {
        var lane = new Lane(dto.Lane?.Length ?? DefaultLaneLength, dto.Lane?.Width ?? DefaultLaneWidth);
        if (lane.Length <= 2f) errors.Add($"lane.length must be over 2 m (got {Format(lane.Length)})");
        if (lane.Width <= 0f) errors.Add($"lane.width must be over 0 (got {Format(lane.Width)})");

        TempoMap? tempo = ReadTempo(dto.Bpm, errors);

        List<ActorSpec> actors = Actors(dto, lane, errors);
        var context = new Context(lane, tempo, actors.Select(a => a.Id).ToHashSet(StringComparer.Ordinal), actors[0].Id, errors);

        List<Phase> phases = Phases(dto, context);
        List<ChartEvent> events = Events(dto, context);

        var lastEvent = events.Count == 0 ? 0 : events.Max(e => e.Time);
        var end = dto.End ?? (lastEvent + DefaultEndPadding);
        if (end < lastEvent) errors.Add($"end ({Format(end)}) is before the last event ({Format(lastEvent)})");

        return new Chart(string.IsNullOrWhiteSpace(dto.Name) ? "Untitled" : dto.Name, lane, actors, phases, events, end)
        {
            Info = new ChartInfo(Blank(dto.Author), Blank(dto.Description), Blank(dto.Difficulty)),
            Arena = ReadArena(dto.Arena, errors),
            Music = ReadMusic(dto.Music, errors),
            Cover = ReadCover(dto.Cover, errors),
            Tracks = Tracks(dto, context),
            Zone = ReadZone(dto.Zone, lane, errors),
            Rules = ReadRules(dto.Rules, errors),
            Tempo = tempo,
            Visuals = ReadVisuals(dto.Visuals, context),
            Colours = ReadColours(dto.Colours ?? dto.Colors, errors, "colours") ?? LaneColours.Default,
        };
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static ArenaSpec ReadArena(ArenaDto? arena, List<string> errors)
    {
        if (arena == null) return ArenaSpec.Here;

        ArenaKind map = ArenaKind.Here;
        if (arena.Map != null && !Enum.TryParse(arena.Map, ignoreCase: true, out map))
        {
            errors.Add($"arena.map \"{arena.Map}\" is not one of {string.Join(", ", Enum.GetNames<ArenaKind>().Select(n => n.ToLowerInvariant()))}");
            return ArenaSpec.Here;
        }

        if (arena.At != null && (arena.At.X == null || arena.At.Z == null))
        {
            errors.Add("arena.at needs x and z");
            return new ArenaSpec(map);
        }

        if (map == ArenaKind.Here && (arena.At != null || arena.Facing != null))
            errors.Add("arena.at and arena.facing need a map - \"here\" is wherever you stand");

        return new ArenaSpec(map, arena.At?.X, arena.At?.Z, arena.Facing);
    }

    private static Zone ReadZone(ZoneDto? zone, Lane lane, List<string> errors)
    {
        var defaults = Zone.For(lane);
        if (zone == null) return defaults;

        var length = zone.Length ?? defaults.Length;
        var height = zone.Height ?? defaults.Height;
        if (length <= 0f || length > lane.Length) errors.Add($"zone.length ({Format(length)}) must be over 0 and up to the lane's length ({Format(lane.Length)})");
        if (height < 1f) errors.Add($"zone.height ({Format(height)}) must be at least 1 m - room to stand");
        return new Zone(length, height);
    }

    private static LaneColours? ReadColours(ColoursDto? colours, List<string> errors, string path)
    {
        if (colours == null) return null;

        Rgba? Read(string? text, string name)
        {
            if (text == null) return null;
            if (Rgba.TryParse(text, out Rgba colour)) return colour;
            errors.Add($"{path}.{name} \"{text}\" is not a colour - use \"#RRGGBB\", \"#RRGGBBAA\" with opacity, or \"none\"");
            return null;
        }

        return new LaneColours(Read(colours.Edges, "edges"), Read(colours.Ends, "ends"), Read(colours.Zone, "zone"), Read(colours.Backstop, "backstop"), Read(colours.Walls, "walls"));
    }

    private static List<VisualCue> ReadVisuals(List<VisualDto>? visuals, Context context)
    {
        var cues = new List<VisualCue>();
        if (visuals == null) return cues;

        foreach ((VisualDto v, var i) in visuals.Select((v, i) => (v, i)))
        {
            var path = $"visuals[{i}]";
            var before = context.Errors.Count;

            var time = Time(v.T, v.Beat, context, path, "t", "beat");
            if (string.IsNullOrWhiteSpace(v.Pack)) context.Errors.Add($"{path} needs a pack - a RumbleReShaded pack's name, e.g. \"UltraShade\"");
            var off = v.Off ?? false;
            if (off && v.Set is { Count: > 0 }) context.Errors.Add($"{path} has both \"off\" and \"set\" - an off fades the pack back to the player's own settings");
            if (!off && v.Set is not { Count: > 0 } && v.On != true) context.Errors.Add($"{path} needs \"set\" (parameter values), \"on\": true or \"off\": true");
            var fade = v.Fade ?? 0;
            if (fade < 0) context.Errors.Add($"{path}.fade cannot be negative (got {Format(fade)})");

            if (context.Errors.Count == before)
                cues.Add(new VisualCue(time!.Value, v.Pack!.Trim(), new Dictionary<string, float>(v.Set ?? [], StringComparer.OrdinalIgnoreCase), off, fade));
        }

        return cues.OrderBy(c => c.Time).ToList();
    }

    private static Rules ReadRules(RulesDto? rules, List<string> errors)
    {
        if (rules == null) return Rules.Default;

        SpawnRule spawn = SpawnRule.All;
        if (rules.Spawn != null && !Enum.TryParse(rules.Spawn, ignoreCase: true, out spawn))
        {
            errors.Add($"rules.spawn \"{rules.Spawn}\" is not one of all, discs, none");
            spawn = SpawnRule.All;
        }

        var cleanup = rules.Cleanup ?? Rules.DefaultCleanupSeconds;
        if (cleanup < 0) errors.Add($"rules.cleanup cannot be negative (got {Format(cleanup)}) - 0 turns it off");
        return new Rules(spawn) { CleanupSeconds = Math.Max(0, cleanup) };
    }

    private static List<Track> Tracks(ChartDto dto, Context context)
    {
        var tracks = new List<Track>();
        if (dto.Tracks == null || dto.Tracks.Count == 0) return tracks;

        if ((dto.Format ?? 1) < TracksFormat)
            context.Errors.Add($"tracks need \"format\": {TracksFormat} or later - set it at the top of the chart");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach ((TrackDto track, var i) in dto.Tracks.Select((t, i) => (t, i)))
        {
            var path = $"tracks[{i}]";
            var actor = track.Actor == null ? context.DefaultActorId : ActorRef(track.Actor, $"{path}.actor", context);
            if (!seen.Add(actor)) context.Errors.Add($"{path}: \"{actor}\" already has a track - one per actor");

            var clips = new List<TrackClip>();
            foreach ((TrackClipDto clip, var j) in (track.Clips ?? []).Select((t, j) => (t, j)))
            {
                var clipPath = $"{path}.clips[{j}]";

                var time = Time(clip.T, clip.Beat, context, clipPath, "t", "beat");
                if (string.IsNullOrWhiteSpace(clip.Clip)) context.Errors.Add($"{clipPath}.clip needs the clip's name (a file in clips/)");
                var from = clip.From ?? 0;
                if (from < 0) context.Errors.Add($"{clipPath}.from cannot be negative (got {Format(from)})");
                if (clip.To is { } to && to <= from) context.Errors.Add($"{clipPath}.to ({Format(to)}) must be after from ({Format(from)})");
                if (time == null || string.IsNullOrWhiteSpace(clip.Clip)) continue;

                if (clips.Count > 0 && time <= clips[^1].Time)
                    context.Errors.Add($"{clipPath}.t ({Format(time.Value)}) must be after the clip before it ({Format(clips[^1].Time)})");
                clips.Add(new TrackClip(time.Value, clip.Clip, from, clip.To) { Mime = clip.Mime ?? false });
            }

            tracks.Add(new Track(actor, clips));
        }

        return tracks;
    }

    private static string? ReadCover(string? cover, List<string> errors)
    {
        if (cover == null) return null;

        var file = cover.Trim();
        if (!ChartPaths.IsSafeRelative(file))
        {
            errors.Add($"cover \"{cover}\" must be a path inside the chart's folder or package");
            return null;
        }

        if (!CoverExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
        {
            errors.Add($"cover \"{cover}\" must be one of {string.Join(", ", CoverExtensions)}");
            return null;
        }

        return file;
    }

    private static MusicSpec? ReadMusic(MusicDto? music, List<string> errors)
    {
        if (music == null) return null;

        var before = errors.Count;
        var file = music.File?.Trim() ?? "";
        if (file.Length == 0) errors.Add("music.file is required - a song next to the chart, e.g. \"song.ogg\"");
        else if (!ChartPaths.IsSafeRelative(file)) errors.Add($"music.file \"{file}\" must be a path inside the chart's folder or package");
        else if (!MusicExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
            errors.Add($"music.file \"{file}\" must be one of {string.Join(", ", MusicExtensions)}");

        var start = music.Start ?? 0;
        if (start < 0) errors.Add($"music.start cannot be negative (got {Format(start)})");

        var volume = music.Volume ?? 1f;
        if (volume is < 0f or > 1f) errors.Add($"music.volume must be 0 to 1 (got {Format(volume)})");

        return errors.Count == before ? new MusicSpec(file, start, volume) : null;
    }

    private record Context(Lane Lane, TempoMap? Tempo, HashSet<string> ActorIds, string DefaultActorId, List<string> Errors);

    /// <summary><c>"bpm": 120</c>, or tempo changes: <c>"bpm": [ { "beat": 0, "bpm": 120 }, { "beat": 64, "bpm": 140 } ]</c>.</summary>
    private static TempoMap? ReadTempo(JsonElement? bpm, List<string> errors)
    {
        if (bpm is not { } value || value.ValueKind == JsonValueKind.Null) return null;

        if (value.ValueKind == JsonValueKind.Number)
        {
            var single = value.GetDouble();
            if (single > 0) return TempoMap.Constant(single);
            errors.Add($"bpm must be over 0 (got {Format(single)})");
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            errors.Add("bpm must be a number, or a list of tempo changes: [ { \"beat\": 0, \"bpm\": 120 }, ... ]");
            return null;
        }

        var changes = new List<TempoChange>();
        var index = 0;
        foreach (JsonElement change in value.EnumerateArray())
        {
            var path = $"bpm[{index++}]";
            if (change.ValueKind != JsonValueKind.Object
                || !TryNumber(change, "beat", out var beat) || !TryNumber(change, "bpm", out var rate))
            {
                errors.Add($"{path} needs a beat and a bpm");
                continue;
            }

            if (rate <= 0) errors.Add($"{path}.bpm must be over 0 (got {Format(rate)})");
            else if (changes.Count == 0 && beat != 0) errors.Add($"{path}.beat must be 0 - the first tempo starts the song");
            else if (changes.Count > 0 && beat <= changes[^1].Beat) errors.Add($"{path}.beat ({Format(beat)}) must be after the change before it ({Format(changes[^1].Beat)})");
            else changes.Add(new TempoChange(beat, rate));
        }

        if (changes.Count == 0)
        {
            if (index == 0) errors.Add("bpm has no tempo changes");
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

    private static List<ActorSpec> Actors(ChartDto dto, Lane lane, List<string> errors)
    {
        if (dto.Actors == null || dto.Actors.Count == 0)
            return [new(DefaultActorId, "Actor", new Spot(0f, lane.Length), DefaultLook)];

        var actors = new List<ActorSpec>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach ((ActorDto actor, var i) in dto.Actors.Select((a, i) => (a, i)))
        {
            var path = $"actors[{i}]";

            var id = string.IsNullOrWhiteSpace(actor.Id) ? (i == 0 ? DefaultActorId : $"actor{i + 1}") : actor.Id;
            if (!seen.Add(id)) errors.Add($"{path}.id \"{id}\" is used twice");

            Spot at = actor.At == null ? new Spot(0f, lane.Length) : ReadSpot(actor.At, $"{path}.at", lane, errors);
            var look = string.IsNullOrWhiteSpace(actor.Look) ? DefaultLook : actor.Look;

            actors.Add(new ActorSpec(id, string.IsNullOrWhiteSpace(actor.Name) ? id : actor.Name, at, look)
            {
                Shiftstones = ReadStones(actor.Shiftstones, $"{path}.shiftstones", errors),
                Measurement = ReadMeasurement(actor.Measurement, $"{path}.measurement", errors),
            });
        }

        return actors;
    }

    /// <summary>"you" (or nothing) for the player's own; else a list of up to two names.</summary>
    private static BodySize? ReadMeasurement(MeasurementDto? measurement, string path, List<string> errors)
    {
        if (measurement == null) return null;

        if (measurement.Height is not (>= 0.5f and <= 3f) || measurement.ArmSpan is not (>= 0.5f and <= 3f))
        {
            errors.Add($"{path} needs a height and armSpan in metres, from 0.5 to 3");
            return null;
        }

        return new BodySize(measurement.Height.Value, measurement.ArmSpan.Value);
    }

    private static List<string>? ReadStones(JsonElement? stones, string path, List<string> errors)
    {
        if (stones is not { } value || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), "you", StringComparison.OrdinalIgnoreCase)) return null;

        if (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(s => s.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(s.GetString())))
        {
            errors.Add($"{path} must be \"you\" or a list of stone names, e.g. [\"Vigor\", \"Flow\"]");
            return null;
        }

        var names = value.EnumerateArray().Select(s => s.GetString()!.Trim()).ToList();
        if (names.Count > 2) errors.Add($"{path} has {names.Count} stones - a player wears two at most");
        return names;
    }

    private static List<Phase> Phases(ChartDto dto, Context context)
    {
        if (dto.Phases == null || dto.Phases.Count == 0) return [new("Battle", 0, 1, null, null)];

        var phases = new List<Phase>();
        foreach ((PhaseDto phase, var i) in dto.Phases.Select((p, i) => (p, i)))
        {
            var path = $"phases[{i}]";

            var start = Time(phase.Start, phase.StartBeat, context, path, "start", "startBeat") ?? 0;
            if (i == 0 && start != 0) context.Errors.Add($"{path} must start at 0 - the first phase starts the battle");
            if (i > 0 && start <= phases[^1].Start)
                context.Errors.Add($"{path}.start ({Format(start)}) must be after phases[{i - 1}] ({Format(phases[^1].Start)})");

            var multiplier = phase.ScoreMultiplier ?? 1;
            if (multiplier <= 0) context.Errors.Add($"{path}.scoreMultiplier must be over 0 (got {Format(multiplier)})");

            var actor = phase.Actor == null ? null : ActorRef(phase.Actor, $"{path}.actor", context);
            Spot? at = phase.At == null ? null : ReadSpot(phase.At, $"{path}.at", context.Lane, context.Errors);

            phases.Add(new Phase(string.IsNullOrWhiteSpace(phase.Name) ? $"Phase {i + 1}" : phase.Name, start, multiplier, actor, at)
            {
                Colours = ReadColours(phase.Colours ?? phase.Colors, context.Errors, $"{path}.colours"),
            });
        }

        return phases;
    }

    private static List<ChartEvent> Events(ChartDto dto, Context context)
    {
        var events = new List<ChartEvent>();
        if (dto.Events == null) return events;

        foreach ((EventDto e, var i) in dto.Events.Select((e, i) => (e, i)))
        {
            var path = $"events[{i}]";

            var time = Time(e.T, e.Beat, context, path, "t", "beat");
            if (time == null) continue;

            var kinds = (e.Projectile != null ? 1 : 0) + (e.Move != null ? 1 : 0) + (e.At != null ? 1 : 0);
            if (kinds != 1)
            {
                context.Errors.Add($"{path} needs exactly one of projectile, move or at (has {kinds})");
                continue;
            }

            var actor = e.Actor == null ? context.DefaultActorId : ActorRef(e.Actor, $"{path}.actor", context);

            if (e.Projectile != null)
            {
                ProjectileEvent? projectile = Projectile(e.Projectile, time.Value, i, $"{path}.projectile", context);
                if (projectile != null) events.Add(projectile);
            }
            else if (e.Move != null)
            {
                if (string.IsNullOrWhiteSpace(e.Move)) context.Errors.Add($"{path}.move needs a move name, e.g. \"Straight\"");
                else events.Add(new MoveEvent(time.Value, i, actor, e.Move));
            }
            else
            {
                events.Add(new PlaceEvent(time.Value, i, actor, ReadSpot(e.At!, $"{path}.at", context.Lane, context.Errors)));
            }
        }

        return events;
    }

    private static ProjectileEvent? Projectile(ProjectileDto p, double time, int order, string path, Context context)
    {
        List<string> errors = context.Errors;
        var before = errors.Count;

        if (string.IsNullOrWhiteSpace(p.Structure)) errors.Add($"{path}.structure is required, e.g. \"Disc\"");

        Vector3 from = Vector3.Zero;
        if (p.From?.X == null || p.From.Y == null || p.From.Z == null)
        {
            errors.Add($"{path}.from needs x, y and z");
        }
        else
        {
            from = new Vector3(p.From.X.Value, p.From.Y.Value, p.From.Z.Value);
            // Shots may come from outside the lane - beyond the boss, or from the sides - but not
            // from behind you, and not from so far that they start out of sight.
            if (from.Z <= 0f || from.Z > context.Lane.Length + ProjectileReach)
                errors.Add($"{path}.from.z ({Format(from.Z)}) must be in front of you and within the lane or up to {Format(ProjectileReach)} m past it: over 0, up to {Format(context.Lane.Length + ProjectileReach)}");
            var side = (context.Lane.Width / 2f) + ProjectileReach;
            if (Math.Abs(from.X) > side)
                errors.Add($"{path}.from.x ({Format(from.X)}) must be within {Format(ProjectileReach)} m of the lane's sides: -{Format(side)} to {Format(side)}");
            if (from.Y < 0f) errors.Add($"{path}.from.y ({Format(from.Y)}) is below the floor");
        }

        Aim aim = Aim.Player;
        if (p.To is JsonElement to)
        {
            if (to.ValueKind == JsonValueKind.String && string.Equals(to.GetString(), "player", StringComparison.OrdinalIgnoreCase))
            {
                aim = Aim.Player;
            }
            else if (to.ValueKind == JsonValueKind.Object
                     && to.TryGetProperty("x", out JsonElement x) && x.TryGetSingle(out var tx)
                     && to.TryGetProperty("y", out JsonElement y) && y.TryGetSingle(out var ty))
            {
                aim = Aim.Point(tx, ty);
            }
            else
            {
                errors.Add($"{path}.to must be \"player\" or {{ \"x\": ..., \"y\": ... }}");
            }
        }

        var speed = p.Speed ?? 0f;
        if (speed <= 0f) errors.Add($"{path}.speed must be over 0 m/s (got {Format(speed)})");

        var telegraph = p.Telegraph ?? DefaultTelegraph;
        if (telegraph < 0f) errors.Add($"{path}.telegraph cannot be negative (got {Format(telegraph)})");
        else if (time - telegraph < 0)
            errors.Add($"{path} would appear before the battle starts: launch at {Format(time)} with a {Format(telegraph)} s telegraph");

        var by = p.By == null ? null : ActorRef(p.By, $"{path}.by", context);

        return errors.Count == before ? new ProjectileEvent(time, order, p.Structure!, from, aim, speed, telegraph, by) { Grounded = p.Grounded } : null;
    }

    private static string ActorRef(string id, string path, Context context)
    {
        if (!context.ActorIds.Contains(id))
            context.Errors.Add($"{path} \"{id}\" is not one of the chart's actors ({string.Join(", ", context.ActorIds)})");
        return id;
    }

    private static Spot ReadSpot(PointDto point, string path, Lane lane, List<string> errors)
    {
        if (point.X == null || point.Z == null)
        {
            errors.Add($"{path} needs x and z");
            return default;
        }

        if (point.Z < 0f || point.Z > lane.Length)
            errors.Add($"{path}.z ({Format(point.Z.Value)}) must be within the lane: 0 to {Format(lane.Length)}");

        return new Spot(point.X.Value, point.Z.Value, point.Facing ?? 0f);
    }

    /// <summary>Seconds from either a seconds field or a beats field - exactly one of them.</summary>
    private static double? Time(double? seconds, double? beats, Context context, string path, string secondsName, string beatsName)
    {
        if (seconds != null && beats != null)
        {
            context.Errors.Add($"{path} has both {secondsName} and {beatsName} - use one");
            return null;
        }

        if (beats != null && context.Tempo == null)
        {
            context.Errors.Add($"{path}.{beatsName} needs the chart's bpm");
            return null;
        }

        if (beats != null) seconds = context.Tempo!.Seconds(beats.Value);

        if (seconds == null)
        {
            context.Errors.Add($"{path} needs a time: {secondsName} (seconds) or {beatsName}");
            return null;
        }

        if (seconds < 0)
        {
            context.Errors.Add($"{path}.{secondsName} cannot be negative (got {Format(seconds.Value)})");
            return null;
        }

        return seconds;
    }

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private class ChartDto
    {
        public int? Format { get; set; }
        public string? Name { get; set; }
        public string? Author { get; set; }
        public string? Description { get; set; }
        public string? Difficulty { get; set; }
        public ArenaDto? Arena { get; set; }
        public MusicDto? Music { get; set; }
        public string? Cover { get; set; }
        public List<TrackDto>? Tracks { get; set; }
        public ZoneDto? Zone { get; set; }
        public RulesDto? Rules { get; set; }
        public LaneDto? Lane { get; set; }
        public JsonElement? Bpm { get; set; }
        public List<ActorDto>? Actors { get; set; }
        public List<PhaseDto>? Phases { get; set; }
        public List<EventDto>? Events { get; set; }
        public List<VisualDto>? Visuals { get; set; }
        public ColoursDto? Colours { get; set; }
        public ColoursDto? Colors { get; set; }
        public double? End { get; set; }
    }

    private class ColoursDto
    {
        public string? Edges { get; set; }
        public string? Ends { get; set; }
        public string? Zone { get; set; }
        public string? Backstop { get; set; }
        public string? Walls { get; set; }
    }

    private class VisualDto
    {
        public double? T { get; set; }
        public double? Beat { get; set; }
        public string? Pack { get; set; }
        public Dictionary<string, float>? Set { get; set; }
        public bool? On { get; set; }
        public bool? Off { get; set; }
        public double? Fade { get; set; }
    }

    private class ZoneDto
    {
        public float? Length { get; set; }
        public float? Height { get; set; }
    }

    private class RulesDto
    {
        public string? Spawn { get; set; }
        public double? Cleanup { get; set; }
    }

    private class TrackDto
    {
        public string? Actor { get; set; }
        public List<TrackClipDto>? Clips { get; set; }
    }

    private class TrackClipDto
    {
        public double? T { get; set; }
        public double? Beat { get; set; }
        public string? Clip { get; set; }
        public double? From { get; set; }
        public double? To { get; set; }
        public bool? Mime { get; set; }
    }

    private class ArenaDto
    {
        public string? Map { get; set; }
        public PointDto? At { get; set; }
        public float? Facing { get; set; }
    }

    private class MusicDto
    {
        public string? File { get; set; }
        public double? Start { get; set; }
        public float? Volume { get; set; }
    }

    private class LaneDto
    {
        public float? Length { get; set; }
        public float? Width { get; set; }
    }

    private class ActorDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public PointDto? At { get; set; }
        public string? Look { get; set; }
        public JsonElement? Shiftstones { get; set; }
        public MeasurementDto? Measurement { get; set; }
    }

    private class MeasurementDto
    {
        public float? Height { get; set; }
        public float? ArmSpan { get; set; }
    }

    private class PhaseDto
    {
        public string? Name { get; set; }
        public double? Start { get; set; }
        public double? StartBeat { get; set; }
        public double? ScoreMultiplier { get; set; }
        public string? Actor { get; set; }
        public PointDto? At { get; set; }
        public ColoursDto? Colours { get; set; }
        public ColoursDto? Colors { get; set; }
    }

    private class EventDto
    {
        public double? T { get; set; }
        public double? Beat { get; set; }
        public string? Actor { get; set; }
        public ProjectileDto? Projectile { get; set; }
        public string? Move { get; set; }
        public PointDto? At { get; set; }
    }

    private class ProjectileDto
    {
        public string? Structure { get; set; }
        public PointDto? From { get; set; }
        public JsonElement? To { get; set; }
        public float? Speed { get; set; }
        public float? Telegraph { get; set; }
        public string? By { get; set; }
        public bool? Grounded { get; set; }
    }

    private class PointDto
    {
        public float? X { get; set; }
        public float? Y { get; set; }
        public float? Z { get; set; }
        public float? Facing { get; set; }
    }
}
