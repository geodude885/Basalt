using System.Globalization;
using Basalt.Services.Audio;
using Basalt.Services.Charts.Model;

namespace Basalt.Services.Charts.Files;

internal class ChartValidator(ChartDto dto)
{
    private static readonly string[] CoverExtensions = { ".png", ".jpg", ".jpeg" };

    private readonly Lane _lane = dto.ToLane();
    private readonly TempoMap? _tempo = dto.ToTempo();
    private readonly List<string> _actorIds = dto.ActorIds();

    public IEnumerable<string> Validate() =>
        ValidateLane()
            .Concat(ValidatePlayerZone())
            .Concat(ValidateTempo())
            .Concat(ValidateArena())
            .Concat(ValidateCover())
            .Concat(ValidateMusic())
            .Concat(ValidateRules())
            .Concat(ValidateActors())
            .Concat(ValidatePhases())
            .Concat(ValidateEvents())
            .Concat(ValidateShaders())
            .Concat(ValidatePerformances())
            .Concat(ValidateColours(dto.Colours, "colours"));

    private IEnumerable<string> ValidateLane()
    {
        if (_lane.Length < 2f)
            yield return "lane.length must be greater than or equal 2m";

        if (_lane.Width <= 0f)
            yield return "lane.width must be greater than 0";

        if (_lane.Gutter < 0f)
            yield return "lane.gutter cannot be negative";
    }

    private IEnumerable<string> ValidatePlayerZone()
    {
        if (dto.PlayerZone is not { } zone)
            yield break;

        if ((float)(zone.Length ?? PlayerZone.For(_lane).Length) <= 0f
            || (float)(zone.Length ?? PlayerZone.For(_lane).Length) > _lane.Length)
            yield return "playerZone.length must be over 0 and up to lane length";

        if ((zone.Height ?? PlayerZone.For(_lane).Height) < 1f)
            yield return "playerZone.height must be at least 1m";
    }

    private IEnumerable<string> ValidateTempo()
    {
        if (dto.Bpm == null)
        {
            if (dto.BpmChanges is { Count: > 0 })
                yield return "bpmChanges needs a bpm to start from";
            yield break;
        }

        if (dto.Bpm <= 0)
        {
            yield return $"bpm must be over 0";
            yield break;
        }

        double lastBeat = 0;
        foreach ((var change, var i) in (dto.BpmChanges ?? []).Select((c, i) => (c, i)))
        {

            if (change.Beat is not { } beat || change.Bpm is not { } rate)
                yield return $"{$"bpmChanges[{i}]"} needs a beat and a bpm";
            else if (rate <= 0)
                yield return $"{$"bpmChanges[{i}]"}.bpm must be over 0";
            else if (beat <= lastBeat)
                yield return $"{$"bpmChanges[{i}]"}.beat ({Format(beat)}) must be after the tempo before it";
            else
                lastBeat = beat;
        }
    }

    private IEnumerable<string> ValidateArena()
    {
        if (dto.Arena is not { } arena) yield break;

        if (arena.Map != null && !Enum.TryParse<ArenaKind>(arena.Map, ignoreCase: true, out _))
            yield return $"arena.map \"{arena.Map}\" is not one of {string.Join(", ", Enum.GetNames<ArenaKind>().Select(n => n.ToLowerInvariant()))}";

        if (arena.At != null && (arena.At.X == null || arena.At.Z == null))
            yield return "arena.at needs x and z";
    }

    private IEnumerable<string> ValidateCover()
    {
        if (dto.Cover is not { } cover) yield break;

        var file = cover.Trim();

        if (!ChartFiles.IsSafeRelative(file))
            yield return $"cover \"{cover}\" must be a path inside the chart's folder or package";
        else if (!CoverExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
            yield return $"cover \"{cover}\" must be one of {string.Join(", ", CoverExtensions)}";
    }

    private IEnumerable<string> ValidateMusic()
    {
        if (dto.Music is not { } music) yield break;

        var file = music.File?.Trim() ?? "";

        if (file.Length == 0)
            yield return "music.file is required";
        else if (!ChartFiles.IsSafeRelative(file))
            yield return $"music.file \"{file}\" must be a path inside the chart's folder or package";
        else if (!AudioDecoder.Extensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
            yield return $"music.file \"{file}\" must be one of {string.Join(", ", AudioDecoder.Extensions)}";

        if (music.Volume is < 0f or > 1f)
            yield return "music.volume must be 0 to 1";
    }

    private IEnumerable<string> ValidateRules()
    {
        if (dto.Rules is not { } rules) yield break;

        if (rules.PlayerSpawns != null && !Enum.TryParse<SpawnRule>(rules.PlayerSpawns, ignoreCase: true, out _))
            yield return $"rules.playerSpawns \"{rules.PlayerSpawns}\" is not one of all, discs, none";

        if (rules.BreakStillAfter < 0)
            yield return "rules.breakStillAfter cannot be negative";
    }

    private IEnumerable<string> ValidateActors()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach ((var actor, var i) in (dto.Actors ?? []).Select((a, i) => (a, i)))
        {
            var id = actor.IdAt(i);

            if (!seen.Add(id))
                yield return $"actors[{i}].id \"{id}\" is used twice";

            if (actor.At != null && (actor.At.X == null || actor.At.Z == null))
                yield return $"actors[{i}].at needs x and z";

            if (actor.Shiftstones?.Any(string.IsNullOrWhiteSpace) == true)
                yield return $"actors[{i}].shiftstones must be a list of stone names, e.g. [\"Vigor\", \"Flow\"]";

            if (actor.Shiftstones?.Count > 2)
                yield return $"actors[{i}].shiftstones cannot have more than 2 shiftstones";

            if (actor.Measurement is { } size && (size.Height is not > 0f || size.ArmSpan is not > 0))
                yield return $"actors[{i}].measurement needs a height and armSpan in metres greater than 0";
        }
    }

    private IEnumerable<string> ValidatePhases()
    {
        double? before = null;
        foreach ((var phase, var i) in (dto.Phases ?? []).Select((p, i) => (p, i)))
        {
            var path = $"phases[{i}]";
            var time = SecondsOrBeatToSeconds(phase.T, phase.Beat) ?? 0;

            if (CheckTimeValue(phase.T, phase.Beat, path) is { } problem)
                yield return problem;

            if (i == 0 && time != 0)
                yield return $"{path} must start at 0";

            if (time <= before)
                yield return $"{path}.t ({Format(time)}) must be after phases[{i - 1}]";

            if (phase.ScoreMultiplier < 0)
                yield return $"{path}.scoreMultiplier must be greater than or equal to 0";

            foreach (var colour in ValidateColours(phase.Colours, $"{path}.colours"))
                yield return colour;

            before = time;
        }
    }

    private IEnumerable<string> ValidateEvents()
    {
        var lastEvent = (dto.Events ?? []).Select(e => SecondsOrBeatToSeconds(e.T, e.Beat)).OfType<double>().DefaultIfEmpty(0).Max();
        if (dto.End < lastEvent)
            yield return $"end ({Format(dto.End.Value)}) is before the last event ({Format(lastEvent)})";

        foreach ((var e, var i) in (dto.Events ?? []).Select((e, i) => (e, i)))
        {

            if (CheckTimeValue(e.T, e.Beat, $"events[{i}]") is { } time)
                yield return time;

            if (e.Actor != null && !_actorIds.Contains(e.Actor))
                yield return $"events[{i}].actor \"{e.Actor}\" is not one of the chart's actors ({string.Join(", ", _actorIds)})";

            var kinds = (e.Projectile != null ? 1 : 0) + (e.Move != null ? 1 : 0) + (e.At != null ? 1 : 0);
            if (kinds != 1)
            {
                yield return $"events[{i}] needs exactly one event type";
                continue;
            }

            if (e.Move != null && string.IsNullOrWhiteSpace(e.Move))
                yield return $"events[{i}].move needs a move name, e.g. \"Straight\"";

            if (e.At != null && (e.At.X == null || e.At.Z == null))
                yield return $"events[{i}].at needs x and z";

            if (e.Projectile != null)
            {
                foreach (var problem in ValidateProjectile(e.Projectile, SecondsOrBeatToSeconds(e.T, e.Beat), $"events[{i}].projectile"))
                    yield return problem;
            }
        }
    }

    private static IEnumerable<string> ValidateProjectile(ProjectileDto p, double? time, string path)
    {
        if (string.IsNullOrWhiteSpace(p.Structure))
            yield return $"{path}.structure is required, e.g. \"Disc\"";

        if (p.From?.X == null || p.From.Y == null || p.From.Z == null)
        {
            yield return $"{path}.from needs x, y and z";
        }
        else
        {
            if (p.From.Z <= 0f)
                yield return $"{path}.from.z must be in greater than 0";

            if (p.From.Y < 0f)
                yield return $"{path}.from.y must be greater than 0";
        }

        if (p.To != null && (p.To.X == null || p.To.Y == null))
            yield return $"{path}.to needs x and y";

        if (p.Offset != null && p.To != null)
            yield return $"{path}.offset can't be used with to";
        else if (p.Offset != null && p.Offset.X == null && p.Offset.Y == null)
            yield return $"{path}.offset needs x or y";

        if ((p.Speed ?? 0f) <= 0f)
            yield return $"{path}.speed must be over 0 m/s";

        if ((float)(p.Telegraph ?? ProjectileEvent.DefaultTelegraph) < 0f)
            yield return $"{path}.telegraph cannot be negative";
        else if (time - (float)(p.Telegraph ?? ProjectileEvent.DefaultTelegraph) < 0)
            yield return $"{path} would appear before the battle starts";
    }

    private IEnumerable<string> ValidateShaders()
    {
        foreach ((var shader, var i) in (dto.Shaders ?? []).Select((s, i) => (s, i)))
        {
            var path = $"shaders[{i}]";

            if (CheckTimeValue(shader.T, shader.Beat, path) is { } time)
                yield return time;

            if (string.IsNullOrWhiteSpace(shader.Pack))
                yield return $"{path} needs a RumbleReshaded pack name e.g. \"BossBattles Stage\"";

            if (shader.Off == true && shader.Values is { Count: > 0 })
                yield return $"{path} can't set values and turn shaders off at the same time";

            if (shader.Fade < 0)
                yield return $"{path}.fade cannot be negative";
        }
    }

    private IEnumerable<string> ValidatePerformances()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach ((var performance, var i) in (dto.Performances ?? []).Select((p, i) => (p, i)))
        {
            var actor = performance.Actor ?? _actorIds[0];

            if (!_actorIds.Contains(actor))
                yield return $"performances[{i}].actor \"{actor}\" is not one of the chart's actors ({string.Join(", ", _actorIds)})";

            if (!seen.Add(actor))
                yield return $"performances[{i}]: \"{actor}\" cannot have more than one performance";

            double? before = null;
            foreach ((var clip, var j) in (performance.Clips ?? []).Select((c, j) => (c, j)))
            {
                var time = SecondsOrBeatToSeconds(clip.T, clip.Beat);

                if (CheckTimeValue(clip.T, clip.Beat, $"performances[{i}].clips[{j}]") is { } problem)
                    yield return problem;

                if (string.IsNullOrWhiteSpace(clip.Clip))
                    yield return $"performances[{i}].clips[{j}].clip needs the clip's name (a file in clips/)";

                if (clip.Start < 0)
                    yield return $"performances[{i}].clips[{j}].start cannot be negative";

                if (clip.End is { } end && end <= (clip.Start ?? 0))
                    yield return $"performances[{i}].clips[{j}].end ({Format(end)}) must be after clip start";

                if (time <= before)
                    yield return $"performances[{i}].clips[{j}].t ({Format(time.Value)}) must be after the clip before it ({Format(before.Value)})";

                before = time;
            }
        }
    }

    private static IEnumerable<string> ValidateColours(ColoursDto? colours, string path)
    {
        if (colours == null) yield break;

        foreach ((var text, var name) in new[] { (colours.SideLines, "sideLines"), (colours.EndLines, "endLines"), (colours.ZoneLine, "zoneLine"), (colours.Backstop, "backstop"), (colours.Walls, "walls") })
        {
            if (text != null && !Rgba.TryParse(text, out _))
                yield return $"{path}.{name} \"{text}\" is not a colour, valid formats are: \"#RRGGBB\", \"#RRGGBBAA\" with opacity, or \"none\"";
        }
    }

    private double? SecondsOrBeatToSeconds(double? t, double? beat)
        => beat is { } beats ? _tempo?.BeatToSeconds(beats) : t;

    private string? CheckTimeValue(double? t, double? beat, string path)
    {
        if (t != null && beat != null) return $"{path} cannot have both t and beat";
        if (beat != null && _tempo == null) return $"{path}.beat needs the chart's bpm";
        if (t == null && beat == null) return $"{path} needs a time in t (seconds) or beat";

        return SecondsOrBeatToSeconds(t, beat) is < 0 ? $"{path}.t cannot be negative" : null;
    }

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
