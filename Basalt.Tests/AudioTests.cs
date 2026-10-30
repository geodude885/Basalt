using Basalt.DevTools.Testing;
using Basalt.Services.Audio;
using Xunit;

namespace Basalt.Tests;

public class AudioDecoderTests
{
    [Theory]
    [InlineData("tone.mp3")]
    [InlineData("tone.ogg")]
    public void DecodesASecondOfStereoTone(string file)
    {
        using var source = AudioDecoder.Open(Fixture(file));
        var audio = TestAudio.DecodeAll(source);

        Assert.Equal(2, audio.Channels);
        Assert.Equal(44100, audio.SampleRate);
        Assert.InRange(audio.Seconds, 0.95, 1.1);
        Assert.InRange(source.Frames, 44100 * 0.95, 44100 * 1.1);

        // A 440 Hz sine at ffmpeg's default level: loud enough, never clipped.
        var rms = Math.Sqrt(audio.Samples.Average(s => s * (double)s));
        Assert.InRange(rms, 0.05, 0.9);
        Assert.All(audio.Samples, s => Assert.InRange(s, -1.01f, 1.01f));
    }

    [Fact]
    public void ReadsInBlocks()
    {
        using var source = AudioDecoder.Open(Fixture("tone.ogg"));
        var block = new float[1000];
        long total = 0;
        int read;
        while ((read = source.ReadSamples(block, 0, block.Length)) > 0) total += read;

        Assert.Equal(source.Frames * source.Channels, total);
    }

    // The music loader's copying carries on, reading these, after its decoding thread has disposed the song.
    [Theory]
    [InlineData("tone.mp3")]
    [InlineData("tone.ogg")]
    public void ASongsShapeOutlivesDisposing(string file)
    {
        var source = AudioDecoder.Open(Fixture(file));
        (var channels, var rate, var frames) = (source.Channels, source.SampleRate, source.Frames);

        source.Dispose();

        Assert.Equal(channels, source.Channels);
        Assert.Equal(rate, source.SampleRate);
        Assert.Equal(frames, source.Frames);
    }

    [Theory]
    [InlineData("song.wav")]
    [InlineData("song.flac")]
    public void OtherFormatsAreRefused(string file) => Assert.Throws<NotSupportedException>(() => AudioDecoder.Open(file));

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
