using System.Numerics;
using System.Text.Json;

namespace BossBattles.Services.Clips;

/// <summary>
/// Clips on disk: JSON, one compact row per frame. Poses are <c>[px, py, pz, qx, qy, qz, qw]</c>,
/// with the lane spot <c>"p"</c>, facing <c>"a"</c> and moves.
/// <code>
/// { "name": "boss-clip-1", "moves": [ { "t": 2.1, "move": "Straight" } ],
///   "frames": [ { "t": 0, "h": [...], "l": [...], "r": [...], "p": [0, 12], "a": 0 }, ... ] }
/// </code>
/// </summary>
public static class ClipFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Write(Clip clip)
    {
        var dto = new ClipDto
        {
            Name = clip.Name,
            Moves = clip.Moves.Count == 0 ? null : clip.Moves.Select(m => new MoveDto { T = MathF.Round(m.Time, 4), Move = m.Move }).ToList(),
            Frames = clip.Frames.Select(f => new FrameDto
            {
                T = MathF.Round(f.Time, 4),
                H = Pose(f.Head),
                L = Pose(f.LeftHand),
                R = Pose(f.RightHand),
                P = f.Offset is { } p ? new[] { MathF.Round(p.X, 4), MathF.Round(p.Y, 4) } : null,
                A = f.Yaw is { } a ? MathF.Round(a, 2) : null,
                F = f.LeftFingers is { } lf && f.RightFingers is { } rf
                    ? new[] { lf.Index, lf.Grip, lf.Thumb, lf.Sway, rf.Index, rf.Grip, rf.Thumb, rf.Sway }.Select(v => MathF.Round(v, 3)).ToArray()
                    : null,
            }).ToList(),
        };

        return JsonSerializer.Serialize(dto, Options);
    }

    /// <summary>A clip, or the reason the text is not one.</summary>
    public static (Clip? Clip, string? Error) Read(string json, string fallbackName)
    {
        ClipDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ClipDto>(json, Options);
        }
        catch (JsonException e)
        {
            return (null, $"not valid clip JSON: {e.Message}");
        }

        if (dto?.Frames == null || dto.Frames.Count == 0) return (null, "the clip has no frames");

        var frames = new List<ClipFrame>(dto.Frames.Count);
        foreach ((FrameDto f, var i) in dto.Frames.Select((f, i) => (f, i)))
        {
            if (!TryPose(f.H, out ClipPose head) || !TryPose(f.L, out ClipPose left) || !TryPose(f.R, out ClipPose right))
            {
                return (null, $"frames[{i}] needs h, l and r as [px, py, pz, qx, qy, qz, qw]");
            }

            if (frames.Count > 0 && f.T < frames[^1].Time) return (null, $"frames[{i}].t ({f.T}) goes back in time");

            frames.Add(new ClipFrame(f.T, head, left, right, f.P is { Length: 2 } ? new Vector2(f.P[0], f.P[1]) : null, f.A,
                f.F is { Length: 8 } ? new ClipFingers(f.F[0], f.F[1], f.F[2], f.F[3]) : null,
                f.F is { Length: 8 } ? new ClipFingers(f.F[4], f.F[5], f.F[6], f.F[7]) : null));
        }

        List<MoveDto> moveDtos = dto.Moves ?? [];
        if (moveDtos.Any(m => string.IsNullOrWhiteSpace(m.Move))) return (null, "a move needs a name");
        var moves = moveDtos.Select(m => new ClipMove(m.T, m.Move!)).ToList();

        return (new Clip(string.IsNullOrWhiteSpace(dto.Name) ? fallbackName : dto.Name, frames) { Moves = moves.OrderBy(m => m.Time).ToList() }, null);
    }

    private static float[] Pose(ClipPose pose) => new[]
    {
        MathF.Round(pose.Position.X, 4), MathF.Round(pose.Position.Y, 4), MathF.Round(pose.Position.Z, 4),
        MathF.Round(pose.Rotation.X, 5), MathF.Round(pose.Rotation.Y, 5), MathF.Round(pose.Rotation.Z, 5), MathF.Round(pose.Rotation.W, 5),
    };

    private static bool TryPose(float[]? values, out ClipPose pose)
    {
        pose = default;
        if (values is not { Length: 7 }) return false;

        pose = new ClipPose(
            new Vector3(values[0], values[1], values[2]),
            Quaternion.Normalize(new Quaternion(values[3], values[4], values[5], values[6])));
        return true;
    }

    private sealed class ClipDto
    {
        public string? Name { get; set; }
        public List<MoveDto>? Moves { get; set; }
        public List<FrameDto>? Frames { get; set; }
    }

    private sealed class MoveDto
    {
        public float T { get; set; }
        public string? Move { get; set; }
    }

    private sealed class FrameDto
    {
        public float T { get; set; }
        public float[]? H { get; set; }
        public float[]? L { get; set; }
        public float[]? R { get; set; }
        public float[]? P { get; set; }
        public float? A { get; set; }
        public float[]? F { get; set; }
    }
}
