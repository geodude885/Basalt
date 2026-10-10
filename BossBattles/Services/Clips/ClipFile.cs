using System.Numerics;
using System.Text.Json;

namespace BossBattles.Services.Clips;

/// <summary>
/// Clips on disk: JSON, one compact row per frame - a recording runs to thousands of frames. Each
/// pose is <c>[px, py, pz, qx, qy, qz, qw]</c>, each stick <c>[x, y]</c>; a take recorded in a lane
/// adds the performer's lane spot <c>"p": [x, z]</c> and facing <c>"a"</c> (degrees from down the
/// lane) per frame, and the moves made.
/// <code>
/// { "name": "boss-take-1", "moves": [ { "t": 2.1, "move": "Straight" } ],
///   "frames": [ { "t": 0, "o": [...], "h": [...], "l": [...], "r": [...], "w": [0, 0], "n": [0, 0], "p": [0, 12] }, ... ] }
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
                O = Pose(f.Offset),
                H = Pose(f.Head),
                L = Pose(f.LeftHand),
                R = Pose(f.RightHand),
                W = new[] { f.Walk.X, f.Walk.Y },
                N = new[] { f.Turn.X, f.Turn.Y },
                P = f.Lane is { } p ? new[] { MathF.Round(p.X, 4), MathF.Round(p.Y, 4) } : null,
                A = f.Yaw is { } a ? MathF.Round(a, 2) : null,
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
        for (var i = 0; i < dto.Frames.Count; i++)
        {
            FrameDto f = dto.Frames[i];
            if (!TryPose(f.O, out ClipPose offset) || !TryPose(f.H, out ClipPose head)
                || !TryPose(f.L, out ClipPose left) || !TryPose(f.R, out ClipPose right))
            {
                return (null, $"frames[{i}] needs o, h, l and r as [px, py, pz, qx, qy, qz, qw]");
            }

            if (i > 0 && f.T < frames[i - 1].Time) return (null, $"frames[{i}].t ({f.T}) goes back in time");

            frames.Add(new ClipFrame(f.T, offset, head, left, right, Stick(f.W), Stick(f.N), f.P is { Length: 2 } ? new Vector2(f.P[0], f.P[1]) : null, f.A));
        }

        var moves = new List<ClipMove>();
        foreach (MoveDto move in dto.Moves ?? [])
        {
            if (string.IsNullOrWhiteSpace(move.Move)) return (null, "a move needs a name");
            moves.Add(new ClipMove(move.T, move.Move));
        }

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

    private static Vector2 Stick(float[]? values) => values is { Length: 2 } ? new Vector2(values[0], values[1]) : Vector2.Zero;

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
        public float[]? O { get; set; }
        public float[]? H { get; set; }
        public float[]? L { get; set; }
        public float[]? R { get; set; }
        public float[]? W { get; set; }
        public float[]? N { get; set; }
        public float[]? P { get; set; }
        public float? A { get; set; }
    }
}
