using System.Numerics;

using Basalt.Services.Clips;

namespace Basalt.DevTools.Testing;

/// <summary>Clips made in code, for tests that must not depend on files.</summary>
public static class SyntheticClips
{
    private const float SampleRate = 30f;
    private const float EyeHeight = 1.6f;

    /// <summary>Standing in a guard, hands swaying side to side; sticks still. No gesture, so no move fires.</summary>
    public static Clip Sway(string name, float seconds)
    {
        var count = Math.Max(2, (int)(seconds * SampleRate) + 1);
        var frames = Enumerable.Range(0, count)
            .Select(i => i / SampleRate)
            .Select(t => (Time: t, Sway: 0.15f * MathF.Sin(t * MathF.PI * 2f)))
            .Select(f => new ClipFrame(
                f.Time,
                new ClipPose(new Vector3(0f, EyeHeight, 0f), Quaternion.Identity),
                new ClipPose(new Vector3(-0.22f + f.Sway, EyeHeight - 0.45f, 0.28f), Quaternion.Identity),
                new ClipPose(new Vector3(0.22f + f.Sway, EyeHeight - 0.45f, 0.28f), Quaternion.Identity)))
            .ToList();

        return new Clip(name, frames);
    }
}
