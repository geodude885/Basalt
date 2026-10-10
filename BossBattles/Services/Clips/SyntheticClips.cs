using System.Numerics;

namespace BossBattles.Services.Clips;

/// <summary>Clips made in code rather than recorded - for the self-test, which must not depend on files.</summary>
public static class SyntheticClips
{
    private const float SampleRate = 30f;
    private const float EyeHeight = 1.6f;

    /// <summary>Standing in a guard, hands swaying side to side; sticks still. No gesture, so no move fires.</summary>
    public static Clip Sway(string name, float seconds)
    {
        var frames = new List<ClipFrame>();
        var count = Math.Max(2, (int)(seconds * SampleRate) + 1);

        for (var i = 0; i < count; i++)
        {
            var t = i / SampleRate;
            var sway = 0.15f * MathF.Sin(t * MathF.PI * 2f);

            frames.Add(new ClipFrame(
                t,
                new ClipPose(Vector3.Zero, Quaternion.Identity),
                new ClipPose(new Vector3(0f, EyeHeight, 0f), Quaternion.Identity),
                new ClipPose(new Vector3(-0.22f + sway, EyeHeight - 0.45f, 0.28f), Quaternion.Identity),
                new ClipPose(new Vector3(0.22f + sway, EyeHeight - 0.45f, 0.28f), Quaternion.Identity),
                Vector2.Zero,
                Vector2.Zero));
        }

        return new Clip(name, frames);
    }
}
