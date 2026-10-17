using BossBattles.Services.Audio;

namespace BossBattles.DevTools.Testing;

/// <summary>Decoded PCM audio: interleaved samples in -1..1.</summary>
public record PcmAudio(float[] Samples, int Channels, int SampleRate)
{
    public double Seconds => Samples.Length / (double)(Channels * SampleRate);
}

/// <summary>Audio made in code for the self-test and the unit tests.</summary>
public static class TestAudio
{
    /// <summary>A whole song decoded; the game streams instead.</summary>
    public static PcmAudio DecodeAll(IPcmSource source)
    {
        var samples = new List<float>((int)Math.Min(int.MaxValue / 2, source.Frames * source.Channels));
        var block = new float[source.SampleRate * source.Channels];
        int read;
        while ((read = source.Read(block, 0, block.Length)) > 0) samples.AddRange(block.Take(read));
        return new PcmAudio(samples.ToArray(), source.Channels, source.SampleRate);
    }

    /// <summary>16-bit PCM.</summary>
    public static byte[] Write(PcmAudio audio)
    {
        var dataBytes = audio.Samples.Length * 2;
        using var stream = new MemoryStream(44 + dataBytes);
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((ushort)1);
        writer.Write((ushort)audio.Channels);
        writer.Write(audio.SampleRate);
        writer.Write(audio.SampleRate * audio.Channels * 2);
        writer.Write((ushort)(audio.Channels * 2));
        writer.Write((ushort)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        foreach (var sample in audio.Samples) writer.Write((short)Math.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue));

        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>A click on every beat over a low hum - something to hear a chart's timing against.</summary>
    public static PcmAudio Metronome(double bpm, double seconds, int sampleRate = 44100)
    {
        var samples = new float[(int)(seconds * sampleRate)];
        var beat = 60.0 / bpm;

        for (var i = 0; i < samples.Length; i++)
        {
            var t = i / (double)sampleRate;
            var sinceBeat = t % beat;
            var click = sinceBeat < 0.05 ? Math.Sin(2 * Math.PI * 1000 * t) * (1 - (sinceBeat / 0.05)) : 0;
            samples[i] = (float)((0.5 * click) + (0.08 * Math.Sin(2 * Math.PI * 110 * t)));
        }

        return new PcmAudio(samples, 1, sampleRate);
    }
}
