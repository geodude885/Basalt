using NLayer;
using NVorbis;

namespace Basalt.Services.Audio;

/// <summary>
/// A song being decoded into interleaved samples, a block at a time. <see cref="Frames"/> is exact
/// for WAV and OGG, estimated for MP3.
/// </summary>
public interface IPcmSource : IDisposable
{
    int Channels { get; }

    int SampleRate { get; }

    long Frames { get; }

    /// <summary>Reads up to <paramref name="count"/> samples (a multiple of <see cref="Channels"/>); 0 at the end.</summary>
    int Read(float[] buffer, int offset, int count);
}

/// <summary>Opens an MP3, OGG or WAV by its extension; the game ships without Unity's own decoders.</summary>
public static class AudioDecoder
{
    // A song with more than `monoAboveSamples` samples in all is downmixed to mono, to save memory.
    public static IPcmSource Open(string path, long monoAboveSamples = long.MaxValue)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".mp3" or ".ogg" or ".wav")) throw new NotSupportedException($"{Path.GetFileName(path)}: songs are .mp3, .ogg or .wav");

        var stream = File.OpenRead(path);
        try
        {
            IPcmSource song = extension switch
            {
                ".mp3" => new Mp3Source(stream),
                ".ogg" => new OggSource(stream),
                _ => WavSource.Open(stream),
            };
            return song.Channels > 1 && song.Frames * song.Channels > monoAboveSamples ? new MonoDownmix(song) : song;
        }
        catch
        {
            // A file that is not what its name says must not stay open (and locked) behind the error.
            stream.Dispose();
            throw;
        }
    }

    private class Mp3Source : IPcmSource
    {
        private readonly MpegFile _file;

        public Mp3Source(Stream stream)
        {
            _file = new MpegFile(stream);
            if (_file.Channels <= 0 || _file.SampleRate <= 0) throw new InvalidDataException("not an MP3");
        }

        public int Channels => _file.Channels;

        public int SampleRate => _file.SampleRate;

        public long Frames => (long)Math.Ceiling(_file.Duration.TotalSeconds * _file.SampleRate);

        public int Read(float[] buffer, int offset, int count) => _file.ReadSamples(buffer, offset, count);

        public void Dispose() => _file.Dispose();
    }

    private class OggSource : IPcmSource
    {
        private readonly VorbisReader _reader;

        public OggSource(Stream stream)
        {
            _reader = new VorbisReader(stream, closeOnDispose: true);
        }

        public int Channels => _reader.Channels;

        public int SampleRate => _reader.SampleRate;

        public long Frames => _reader.TotalSamples;

        public int Read(float[] buffer, int offset, int count) => _reader.ReadSamples(buffer, offset, count);

        public void Dispose() => _reader.Dispose();
    }

    private class MonoDownmix : IPcmSource
    {
        private readonly IPcmSource _song;
        private float[] _interleaved = Array.Empty<float>();

        public MonoDownmix(IPcmSource song)
        {
            _song = song;
        }

        public int Channels => 1;

        public int SampleRate => _song.SampleRate;

        public long Frames => _song.Frames;

        public int Read(float[] buffer, int offset, int count)
        {
            var wanted = count * _song.Channels;
            if (_interleaved.Length < wanted) _interleaved = new float[wanted];

            var frames = _song.Read(_interleaved, 0, wanted) / _song.Channels;
            // Plain loops: this runs for every sample of a long song, where LINQ would allocate per frame.
            for (var frame = 0; frame < frames; frame++)
            {
                var sum = 0f;
                for (var channel = 0; channel < _song.Channels; channel++) sum += _interleaved[(frame * _song.Channels) + channel];
                buffer[offset + frame] = sum / _song.Channels;
            }

            return frames;
        }

        public void Dispose() => _song.Dispose();
    }
}

/// <summary>A WAV read a block at a time: PCM 8/16/24/32-bit or 32-bit float.</summary>
internal class WavSource : IPcmSource
{
    private const ushort PcmFormat = 1;
    private const ushort FloatFormat = 3;
    private const ushort ExtensibleFormat = 0xFFFE;

    private readonly Stream _stream;
    private readonly ushort _format;
    private readonly int _bytesPerSample;
    private long _left;
    private byte[] _raw = Array.Empty<byte>();

    private WavSource(Stream stream, ushort format, int channels, int rate, int bits, long dataBytes)
    {
        _stream = stream;
        _format = format;
        Channels = channels;
        SampleRate = rate;
        _bytesPerSample = bits / 8;
        _left = dataBytes;
        Frames = dataBytes / (_bytesPerSample * channels);
    }

    public int Channels { get; }

    public int SampleRate { get; }

    public long Frames { get; }

    public static WavSource Open(Stream stream)
    {
        var reader = new BinaryReader(stream);
        if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("not a RIFF/WAVE file");
        reader.ReadInt32();
        if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("not a RIFF/WAVE file");

        ushort format = 0, channels = 0, bits = 0;
        var rate = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            string id = new(reader.ReadChars(4));
            long size = reader.ReadUInt32();
            var body = stream.Position;

            if (id == "fmt ")
            {
                format = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                rate = reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadUInt16();
                bits = reader.ReadUInt16();
                if (format == ExtensibleFormat && size >= 26)
                {
                    reader.ReadBytes(8);
                    format = reader.ReadUInt16();
                }
            }
            else if (id == "data")
            {
                var known = (format == PcmFormat && bits is 8 or 16 or 24 or 32) || (format == FloatFormat && bits == 32);
                return channels == 0 || rate <= 0 || !known
                    ? throw new InvalidDataException($"unsupported WAV: format {format}, {bits}-bit")
                    : new WavSource(stream, format, channels, rate, bits, Math.Min(size, stream.Length - body));
            }

            stream.Position = body + size + (size & 1);
        }

        throw new InvalidDataException("no data chunk");
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var samples = (int)Math.Min(count, _left / _bytesPerSample);
        if (samples <= 0) return 0;

        var bytes = samples * _bytesPerSample;
        if (_raw.Length < bytes) _raw = new byte[bytes];
        var got = 0;
        while (got < bytes)
        {
            var n = _stream.Read(_raw, got, bytes - got);
            if (n <= 0) break;
            got += n;
        }

        samples = got / _bytesPerSample;
        _left -= got;
        for (var i = 0; i < samples; i++)
        {
            var at = i * _bytesPerSample;
            buffer[offset + i] = (_format, _bytesPerSample) switch
            {
                (PcmFormat, 1) => (_raw[at] - 128) / 128f,
                (PcmFormat, 2) => BitConverter.ToInt16(_raw, at) / 32768f,
                (PcmFormat, 3) => (((_raw[at] << 8) | (_raw[at + 1] << 16) | (_raw[at + 2] << 24)) >> 8) / 8388608f,
                (PcmFormat, 4) => BitConverter.ToInt32(_raw, at) / 2147483648f,
                _ => BitConverter.ToSingle(_raw, at),
            };
        }

        return samples;
    }

    public void Dispose() => _stream.Dispose();
}
