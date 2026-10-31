using NLayer;
using NVorbis;

namespace Basalt.Services.Audio;

// A song being decoded into samples
public interface IPcmSource : IDisposable
{
    int Channels { get; }
    int SampleRate { get; }
    long Frames { get; }

    int ReadSamples(float[] buffer, int offset, int count);
}

public static class AudioDecoder
{
    public static readonly IReadOnlyList<string> Extensions = new[] { ".ogg", ".mp3" };

    public static IPcmSource Open(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (!Extensions.Contains(extension)) throw new NotSupportedException($"{Path.GetFileName(path)}: songs are .mp3 or .ogg");

        var stream = File.OpenRead(path);
        try
        {
            return extension == ".mp3" ? new Mp3Source(stream) : new OggSource(stream);
        }
        catch
        {
            stream.Dispose(); // File isn't what it said it was, maybe don't trust it
            throw;
        }
    }

    private class Mp3Source : IPcmSource
    {
        public int Channels { get; }
        public int SampleRate { get; }
        public long Frames { get; }

        public int ReadSamples(float[] buffer, int offset, int count) => _file.ReadSamples(buffer, offset, count);

        private readonly MpegFile _file;

        public Mp3Source(Stream stream)
        {
            _file = new MpegFile(stream);
            if (_file.Channels <= 0 || _file.SampleRate <= 0) throw new InvalidDataException("not an MP3");
            Channels = _file.Channels;
            SampleRate = _file.SampleRate;
            Frames = (long)Math.Ceiling(_file.Duration.TotalSeconds * _file.SampleRate);
        }

        public void Dispose() => _file.Dispose();
    }

    private class OggSource : IPcmSource
    {
        public int Channels { get; }
        public int SampleRate { get; }
        public long Frames { get; }

        public int ReadSamples(float[] buffer, int offset, int count) => _reader.ReadSamples(buffer, offset, count);

        private readonly VorbisReader _reader;

        public OggSource(Stream stream)
        {
            _reader = new VorbisReader(stream, closeOnDispose: true);
            Channels = _reader.Channels;
            SampleRate = _reader.SampleRate;
            Frames = _reader.TotalSamples;
        }

        public void Dispose() => _reader.Dispose();
    }
}
