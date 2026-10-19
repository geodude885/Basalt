using Basalt.DevTools.Testing;
using Basalt.Services.Audio;
using Xunit;

namespace Basalt.Tests;

public class AudioDecoderTests
{
    [Theory]
    [InlineData("tone.mp3")]
    [InlineData("tone.ogg")]
    [InlineData("tone.wav")]
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
        using var source = AudioDecoder.Open(Fixture("tone.wav"));
        var block = new float[1000];
        long total = 0;
        int read;
        while ((read = source.Read(block, 0, block.Length)) > 0) total += read;

        Assert.Equal(source.Frames * source.Channels, total);
    }

    [Theory]
    [InlineData("tone.mp3")]
    [InlineData("tone.ogg")]
    [InlineData("tone.wav")]
    public void LongSongsAreDownmixedToMono(string file)
    {
        using var stereo = AudioDecoder.Open(Fixture(file));
        var stereoAudio = TestAudio.DecodeAll(stereo);
        using var mono = AudioDecoder.Open(Fixture(file), monoAboveSamples: 1000);
        var monoAudio = TestAudio.DecodeAll(mono);

        Assert.Equal(1, monoAudio.Channels);
        Assert.Equal(stereo.Frames, mono.Frames);
        Assert.Equal(stereoAudio.Samples.Length / 2, monoAudio.Samples.Length);
        Assert.Equal((stereoAudio.Samples[200] + stereoAudio.Samples[201]) / 2f, monoAudio.Samples[100], 5);
    }

    [Fact]
    public void ShortSongsStayStereo()
    {
        using var source = AudioDecoder.Open(Fixture("tone.wav"), monoAboveSamples: 1_000_000);

        Assert.Equal(2, source.Channels);
    }

    [Fact]
    public void OtherFormatsAreRefused() => Assert.Throws<NotSupportedException>(() => AudioDecoder.Open("song.flac"));

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
