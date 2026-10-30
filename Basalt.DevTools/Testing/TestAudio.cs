using Basalt.Services.Audio;

namespace Basalt.DevTools.Testing;

/// <summary>Decoded PCM audio: interleaved samples in -1..1.</summary>
public record PcmAudio(float[] Samples, int Channels, int SampleRate)
{
    public double Seconds => Samples.Length / (double)(Channels * SampleRate);
}

/// <summary>Audio decoded whole, for the unit tests.</summary>
public static class TestAudio
{
    /// <summary>A whole song decoded; the game streams instead.</summary>
    public static PcmAudio DecodeAll(IPcmSource source)
    {
        var samples = new List<float>((int)Math.Min(int.MaxValue / 2, source.Frames * source.Channels));
        var block = new float[source.SampleRate * source.Channels];
        int read;
        while ((read = source.ReadSamples(block, 0, block.Length)) > 0) samples.AddRange(block.Take(read));
        return new PcmAudio(samples.ToArray(), source.Channels, source.SampleRate);
    }
}
