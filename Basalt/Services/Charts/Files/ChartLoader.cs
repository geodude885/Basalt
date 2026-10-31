using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Basalt.Services.Audio;
using Basalt.Services.Charts.Model;

namespace Basalt.Services.Charts.Files;

public record ChartLoadResult(Chart? Chart, IReadOnlyList<string> Errors)
{
    public bool Ok => Chart != null;
}

public static class ChartLoader
{
    public const int NewestFormat = 3;

    public static ChartLoadResult Load(string json)
    {
        ChartDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ChartDto>(json, ChartJson.JsonOptions);
        }
        catch (JsonException e)
        {
            return new ChartLoadResult(null, [$"not valid chart JSON: {e.Message}"]);
        }

        if (dto == null) return new ChartLoadResult(null, ["the file is empty"]);

        if (dto.Format is > NewestFormat)
            return new ChartLoadResult(null, [$"made for a newer Basalt (chart format {dto.Format}; this one reads up to {NewestFormat}) - update the mod"]);

        var reader = new ChartReader(dto);
        var chart = reader.Build();
        return reader.Errors.Count == 0 ? new ChartLoadResult(chart, reader.Errors) : new ChartLoadResult(null, reader.Errors);
    }
}

// Builds chart from its parsed JSON
internal class ChartReader(ChartDto dto)
{
    public List<string> Errors { get; } = [];

    private static readonly string[] CoverExtensions = { ".png", ".jpg", ".jpeg" };
    private Lane _lane;
    private TempoMap? _tempo;
    private HashSet<string> _actorIds = [];
    private string _defaultActorId = ActorSpec.DefaultId;


    public Chart Build()
    {
        _lane = new Lane(dto.Lane?.Length ?? Lane.Default.Length, dto.Lane?.Width ?? Lane.Default.Width);
        if (_lane.Length <= 2f) Errors.Add($"lane.length must be over 2 m (got {Format(_lane.Length)})");
        if (_lane.Width <= 0f) Errors.Add($"lane.width must be over 0 (got {Format(_lane.Width)})");

        _tempo = ReadTempo(dto.Bpm, dto.BpmChanges);

        var actors = Actors();
        _actorIds = actors.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        _defaultActorId = actors[0].Id;

        var phases = Phases();
        var events = Events();

        var lastEvent = events.Count == 0 ? 0 : events.Max(e => e.Time);
        var end = dto.End ?? (lastEvent + Chart.DefaultEndPadding);
        if (end < lastEvent) Errors.Add($"end ({Format(end)}) is before the last event ({Format(lastEvent)})");

        return new Chart(string.IsNullOrWhiteSpace(dto.Name) ? "Untitled" : dto.Name, _lane, actors, phases, events, end)
        {
            Info = new ChartInfo(
                Blank(dto.Author),
                Blank(dto.Description),
                Blank(dto.Difficulty)),
            Arena = ReadArena(dto.Arena),
            Music = ReadMusic(dto.Music),
            Cover = ReadCover(dto.Cover),
            Performances = Performances(),
            PlayerZone = ReadPlayerZone(dto.PlayerZone),
            Rules = ReadRules(dto.Rules),
            Tempo = _tempo,
            Shaders = ReadShaders(dto.Shaders),
            Colours = ReadColours(dto.Colours, "colours") ?? LaneColours.Default,
        };
    }

    private ArenaSpec ReadArena(ArenaDto? arena)
    {
        if (arena == null) return ArenaSpec.Default;

        var map = ArenaSpec.Default.Map;
        if (arena.Map != null && !Enum.TryParse(arena.Map, ignoreCase: true, out map))
        {
            Errors.Add($"arena.map \"{arena.Map}\" is not one of {string.Join(", ", Enum.GetNames<ArenaKind>().Select(n => n.ToLowerInvariant()))}");
            return ArenaSpec.Default;
        }

        if (arena.At != null && (arena.At.X == null || arena.At.Z == null))
        {
            Errors.Add("arena.at needs x and z");
            return new ArenaSpec(map);
        }

        return new ArenaSpec(map, arena.At?.X ?? 0f, arena.At?.Z ?? 0f, arena.At?.Facing ?? 0f);
    }

    private PlayerZone ReadPlayerZone(PlayerZoneDto? zone)
    {
        var defaults = PlayerZone.For(_lane);
        if (zone == null) return defaults;

        var length = zone.Length ?? defaults.Length;
        var height = zone.Height ?? defaults.Height;
        if (length <= 0f || length > _lane.Length) Errors.Add($"playerZone.length ({Format(length)}) must be over 0 and up to the lane's length ({Format(_lane.Length)})");
        if (height < 1f) Errors.Add($"playerZone.height ({Format(height)}) must be at least 1 m - room to stand");
        return new PlayerZone(length, height);
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

        return new LaneColours(Read(colours.SideLines, "sideLines"), Read(colours.EndLines, "endLines"), Read(colours.ZoneLine, "zoneLine"), Read(colours.Backstop, "backstop"), Read(colours.Walls, "walls"));
    }

    private List<ShaderCue> ReadShaders(List<ShaderDto>? shaders)
    {
        var cues = new List<ShaderCue>();
        if (shaders == null) return cues;

        foreach ((var v, var i) in shaders.Select((v, i) => (v, i)))
        {
            var path = $"shaders[{i}]";
            var before = Errors.Count;

            var time = Time(v.T, v.Beat, path);
            if (string.IsNullOrWhiteSpace(v.Pack)) Errors.Add($"{path} needs a pack - a RumbleReShaded pack's name, e.g. \"UltraShade\"");
            var off = v.Off ?? false;
            if (off && v.Values is { Count: > 0 }) Errors.Add($"{path} has both \"off\" and \"values\" - an off fades the pack back to the player's own settings");
            var fade = v.Fade ?? 0;
            if (fade < 0) Errors.Add($"{path}.fade cannot be negative (got {Format(fade)})");

            if (Errors.Count == before)
                cues.Add(new ShaderCue(time!.Value, v.Pack!.Trim(), new Dictionary<string, float>(v.Values ?? [], StringComparer.OrdinalIgnoreCase), off, fade));
        }

        return cues.OrderBy(c => c.Time).ToList();
    }

    private Rules ReadRules(RulesDto? rules)
    {
        if (rules == null) return Rules.Default;

        var spawn = SpawnRule.All;
        if (rules.PlayerSpawns != null && !Enum.TryParse(rules.PlayerSpawns, ignoreCase: true, out spawn))
        {
            Errors.Add($"rules.playerSpawns \"{rules.PlayerSpawns}\" is not one of all, discs, none");
            spawn = SpawnRule.All;
        }

        var breakStillAfter = rules.BreakStillAfter ?? Rules.DefaultBreakStillAfterSeconds;
        if (breakStillAfter < 0) Errors.Add($"rules.breakStillAfter cannot be negative (got {Format(breakStillAfter)}) - 0 turns it off");
        return new Rules(spawn) { BreakStillAfterSeconds = Math.Max(0, breakStillAfter), BreakBehind = rules.BreakBehind ?? false };
    }

    private List<ActorPerformance> Performances()
    {
        var performances = new List<ActorPerformance>();
        if (dto.Performances == null || dto.Performances.Count == 0) return performances;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach ((var performance, var i) in dto.Performances.Select((t, i) => (t, i)))
        {
            var path = $"performances[{i}]";
            var actor = performance.Actor == null ? _defaultActorId : ActorRef(performance.Actor, $"{path}.actor");
            if (!seen.Add(actor)) Errors.Add($"{path}: \"{actor}\" already has a performance - one per actor");

            var clips = new List<PerformanceClip>();
            foreach ((var clip, var j) in (performance.Clips ?? []).Select((t, j) => (t, j)))
            {
                var clipPath = $"{path}.clips[{j}]";

                var time = Time(clip.T, clip.Beat, clipPath);
                if (string.IsNullOrWhiteSpace(clip.Clip)) Errors.Add($"{clipPath}.clip needs the clip's name (a file in clips/)");
                var start = clip.Start ?? 0;
                if (start < 0) Errors.Add($"{clipPath}.start cannot be negative (got {Format(start)})");
                if (clip.End is { } end && end <= start) Errors.Add($"{clipPath}.end ({Format(end)}) must be after start ({Format(start)})");
                if (time == null || string.IsNullOrWhiteSpace(clip.Clip)) continue;

                if (clips.Count > 0 && time <= clips[^1].Time)
                    Errors.Add($"{clipPath}.t ({Format(time.Value)}) must be after the clip before it ({Format(clips[^1].Time)})");
                clips.Add(new PerformanceClip(time.Value, clip.Clip, start, clip.End) { Mime = clip.Mime ?? false });
            }

            performances.Add(new ActorPerformance(actor, clips));
        }

        return performances;
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
        else if (!AudioDecoder.Extensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
            Errors.Add($"music.file \"{file}\" must be one of {string.Join(", ", AudioDecoder.Extensions)}");

        var offset = music.Offset ?? 0;

        var volume = music.Volume ?? 1f;
        if (volume is < 0f or > 1f) Errors.Add($"music.volume must be 0 to 1 (got {Format(volume)})");

        return Errors.Count == before ? new MusicSpec(file, offset, volume) : null;
    }

    private TempoMap? ReadTempo(double? bpm, List<BpmChangeDto>? changes)
    {
        if (bpm == null)
        {
            if (changes is { Count: > 0 }) Errors.Add("bpmChanges needs a bpm to start from");
            return null;
        }

        if (bpm <= 0)
        {
            Errors.Add($"bpm must be over 0 (got {Format(bpm.Value)})");
            return null;
        }

        var sections = new List<TempoSection> { new(0, bpm.Value) };
        foreach ((var change, var i) in (changes ?? []).Select((c, i) => (c, i)))
        {
            var path = $"bpmChanges[{i}]";
            if (change.Beat is not { } beat || change.Bpm is not { } rate) Errors.Add($"{path} needs a beat and a bpm");
            else if (rate <= 0) Errors.Add($"{path}.bpm must be over 0 (got {Format(rate)})");
            else if (beat <= sections[^1].Beat) Errors.Add($"{path}.beat ({Format(beat)}) must be after the tempo before it ({Format(sections[^1].Beat)})");
            else sections.Add(new TempoSection(beat, rate));
        }

        return new TempoMap(sections);
    }

    private List<ActorSpec> Actors()
    {
        if (dto.Actors == null || dto.Actors.Count == 0)
            return [new(ActorSpec.DefaultId, "Actor", new Spot(0f, _lane.Length), ActorSpec.DefaultLook)];

        var actors = new List<ActorSpec>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach ((var actor, var i) in dto.Actors.Select((a, i) => (a, i)))
        {
            var path = $"actors[{i}]";

            var id = string.IsNullOrWhiteSpace(actor.Id) ? (i == 0 ? ActorSpec.DefaultId : $"actor{i + 1}") : actor.Id;
            if (!seen.Add(id)) Errors.Add($"{path}.id \"{id}\" is used twice");

            var at = actor.At == null ? new Spot(0f, _lane.Length) : ReadSpot(actor.At, $"{path}.at");
            var look = string.IsNullOrWhiteSpace(actor.Look) ? ActorSpec.DefaultLook : actor.Look;

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

    private IReadOnlyList<string> ReadStones(List<string>? stones, string path)
    {
        if (stones == null) return Array.Empty<string>();

        if (stones.Any(string.IsNullOrWhiteSpace)) Errors.Add($"{path} must be a list of stone names, e.g. [\"Vigor\", \"Flow\"]");
        if (stones.Count > 2) Errors.Add($"{path} has {stones.Count} stones - a player wears two at most");
        return stones.Select(s => s?.Trim() ?? "").ToList();
    }

    private List<Phase> Phases()
    {
        if (dto.Phases == null || dto.Phases.Count == 0) return [new("Battle", 0, 1)];

        var phases = new List<Phase>();
        foreach ((var phase, var i) in dto.Phases.Select((p, i) => (p, i)))
        {
            var path = $"phases[{i}]";

            var time = Time(phase.T, phase.Beat, path) ?? 0;
            if (i == 0 && time != 0) Errors.Add($"{path} must start at 0 - the first phase starts the battle");
            if (i > 0 && time <= phases[^1].Time)
                Errors.Add($"{path}.t ({Format(time)}) must be after phases[{i - 1}] ({Format(phases[^1].Time)})");

            var multiplier = phase.ScoreMultiplier ?? 1;
            if (multiplier <= 0) Errors.Add($"{path}.scoreMultiplier must be over 0 (got {Format(multiplier)})");

            phases.Add(new Phase(string.IsNullOrWhiteSpace(phase.Name) ? $"Phase {i + 1}" : phase.Name, time, multiplier)
            {
                Colours = ReadColours(phase.Colours, $"{path}.colours"),
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
                var projectile = Projectile(e.Projectile, time.Value, i, $"{path}.projectile", actor);
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

    private ProjectileEvent? Projectile(ProjectileDto p, double time, int order, string path, string actor)
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

            if (from.Z <= 0f || from.Z > _lane.Length + Lane.ProjectileReach)
                Errors.Add($"{path}.from.z ({Format(from.Z)}) must be in front of you and within the lane or up to {Format(Lane.ProjectileReach)} m past it: over 0, up to {Format(_lane.Length + Lane.ProjectileReach)}");
            var side = (_lane.Width / 2f) + Lane.ProjectileReach;
            if (Math.Abs(from.X) > side)
                Errors.Add($"{path}.from.x ({Format(from.X)}) must be within {Format(Lane.ProjectileReach)} m of the lane's sides: -{Format(side)} to {Format(side)}");
            if (from.Y < 0f) Errors.Add($"{path}.from.y ({Format(from.Y)}) is below the floor");
        }

        var aim = Aim.Player;
        if (p.To != null)
        {
            if (p.To.X == null || p.To.Y == null) Errors.Add($"{path}.to needs x and y");
            else aim = Aim.Point(p.To.X.Value, p.To.Y.Value);
        }

        var speed = p.Speed ?? 0f;
        if (speed <= 0f) Errors.Add($"{path}.speed must be over 0 m/s (got {Format(speed)})");

        var telegraph = p.Telegraph ?? ProjectileEvent.DefaultTelegraph;
        if (telegraph < 0f) Errors.Add($"{path}.telegraph cannot be negative (got {Format(telegraph)})");
        else if (time - telegraph < 0)
            Errors.Add($"{path} would appear before the battle starts: launch at {Format(time)} with a {Format(telegraph)} s telegraph");

        return Errors.Count == before ? new ProjectileEvent(time, order, p.Structure!, from, aim, speed, telegraph, actor) { Grounded = p.Grounded } : null;
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
    private double? Time(double? seconds, double? beats, string path)
    {
        if (seconds != null && beats != null)
        {
            Errors.Add($"{path} has both t and beat - use one");
            return null;
        }

        if (beats != null && _tempo == null)
        {
            Errors.Add($"{path}.beat needs the chart's bpm");
            return null;
        }

        if (beats != null) seconds = _tempo!.BeatToSeconds(beats.Value);

        if (seconds == null)
        {
            Errors.Add($"{path} needs a time: t (seconds) or beat");
            return null;
        }

        if (seconds < 0)
        {
            Errors.Add($"{path}.t cannot be negative (got {Format(seconds.Value)})");
            return null;
        }

        return seconds;
    }
    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
