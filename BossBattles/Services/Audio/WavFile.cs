namespace BossBattles.Services.Audio;

/// <summary>Decoded PCM audio: interleaved samples in -1..1.</summary>
public sealed record PcmAudio(float[] Samples, int Channels, int SampleRate)
{
    public double Seconds => Samples.Length / (double)(Channels * SampleRate);
}

/// <summary>
/// Reads and writes plain PCM WAV (8/16/24/32-bit integer, 32-bit float). The game loads songs
/// with Unity's own decoder; this is the fallback for WAV should that fail under the interop, and
/// what the self-test writes its test tone with.
/// </summary>
public static class WavFile
{
    private const ushort PcmFormat = 1;
    private const ushort FloatFormat = 3;
    private const ushort ExtensibleFormat = 0xFFFE;

    public static (PcmAudio? Audio, string? Error) Read(byte[] data)
    {
        if (data.Length < 12 || Tag(data, 0) != "RIFF" || Tag(data, 8) != "WAVE") return (null, "not a RIFF/WAVE file");

        ushort format = 0, channels = 0, bits = 0;
        var rate = 0;
        var position = 12;

        while (position + 8 <= data.Length)
        {
            var id = Tag(data, position);
            var size = BitConverter.ToInt32(data, position + 4);
            var body = position + 8;
            if (size < 0 || body + size > data.Length) size = data.Length - body;

            if (id == "fmt " && size >= 16)
            {
                format = BitConverter.ToUInt16(data, body);
                channels = BitConverter.ToUInt16(data, body + 2);
                rate = BitConverter.ToInt32(data, body + 4);
                bits = BitConverter.ToUInt16(data, body + 14);
                if (format == ExtensibleFormat && size >= 26) format = BitConverter.ToUInt16(data, body + 24);
            }
            else if (id == "data")
            {
                if (channels == 0 || rate <= 0) return (null, "data before fmt");
                var samples = Decode(data, body, size, format, bits);
                return samples == null
                    ? (null, $"unsupported encoding: format {format}, {bits}-bit")
                    : (new PcmAudio(samples, channels, rate), null);
            }

            position = body + size + (size & 1);
        }

        return (null, "no data chunk");
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
        writer.Write(PcmFormat);
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

    private static float[]? Decode(byte[] data, int offset, int size, ushort format, ushort bits)
    {
        var bytes = bits / 8;
        if (bytes == 0) return null;

        var samples = new float[size / bytes];
        for (var i = 0; i < samples.Length; i++)
        {
            var at = offset + (i * bytes);
            samples[i] = (format, bits) switch
            {
                (PcmFormat, 8) => (data[at] - 128) / 128f,
                (PcmFormat, 16) => BitConverter.ToInt16(data, at) / 32768f,
                (PcmFormat, 24) => (((data[at] << 8) | (data[at + 1] << 16) | (data[at + 2] << 24)) >> 8) / 8388608f,
                (PcmFormat, 32) => BitConverter.ToInt32(data, at) / 2147483648f,
                (FloatFormat, 32) => BitConverter.ToSingle(data, at),
                _ => float.NaN,
            };

            if (float.IsNaN(samples[i])) return null;
        }

        return samples;
    }

    private static string Tag(byte[] data, int at) => at + 4 <= data.Length ? System.Text.Encoding.ASCII.GetString(data, at, 4) : "";
}
