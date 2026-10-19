using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using Basalt.Services.Audio;
using MelonLoader;
using UnityEngine;
using UnityEngine.Audio;

namespace Basalt.Presentation;

internal class MusicController
{
    private const double ScheduleAheadSeconds = 0.15;
    private const float FadeSeconds = 1.5f;

    private const float MaxVolume = 0.7f;
    private const long MonoAboveSamples = 32_000_000;

    private const int ChunksDecodedAhead = 30;
    private const int ChunksCopiedPerFrame = 4;

    private (string Path, DateTime Written, AudioClip Clip)? _lastDecoded;

    private AudioSource? _source;
    private AudioClip? _clip;
    private double _dspStart;
    private bool _started;
    private int _loadNumber;

    public bool Loading { get; private set; }

    public float Progress { get; private set; }

    // The player's music volume; the chart's own multiplies it.
    public float Volume { get; set; } = 1f;

    public bool Ended => _started && _clip != null && AudioSettings.dspTime - _dspStart > _clip.length + 0.5;

    // Seconds since the song started (negative just before); null with no song playing.
    public double? Clock => _started ? AudioSettings.dspTime - _dspStart : null;

    public void Load(string path)
    {
        Clear();

        if (!File.Exists(path))
        {
            Logger.Warning($"[Music.Missing] no song at {path} - playing without music");
            return;
        }

        var written = File.GetLastWriteTimeUtc(path);
        if (_lastDecoded is { } last && string.Equals(last.Path, path, StringComparison.OrdinalIgnoreCase) && last.Written == written && last.Clip != null)
        {
            _clip = last.Clip;
            Progress = 1f;
            Logger.Debug($"[Music.Loaded] {Path.GetFileName(path)}: already decoded");
            return;
        }

        Loading = true;
        MelonCoroutines.Start(Decode(path, written, _loadNumber));
    }

    // Clock runs from `start` seconds into the song.
    public void Play(double start, float chartVolume)
    {
        if (_clip == null || !EnsureSource()) return;

        _source!.Stop();
        _source.clip = _clip;
        _source.volume = Mathf.Clamp01(Volume * chartVolume * MaxVolume);
        _source.time = (float)Math.Clamp(start, 0, Math.Max(0, _clip.length - 0.01));
        _dspStart = AudioSettings.dspTime + ScheduleAheadSeconds;
        _source.PlayScheduled(_dspStart);
        _started = true;
        Logger.Debug($"[Music.Playing] {_clip.name} from {start:0.00}s at volume {_source.volume:0.00}");
    }

    // The clock keeps running until Stop.
    public void FadeOut()
    {
        if (_source != null && _source.isPlaying) MelonCoroutines.Start(Fade(_source, _loadNumber));
    }

    public void Stop()
    {
        _source?.Stop();
        _started = false;
    }

    public void Clear()
    {
        Stop();
        _loadNumber++;
        _clip = null;
        Progress = 0f;
        Loading = false;
    }

    // A later Load or Clear supersedes this one, and it stops where it is.
    private IEnumerator Decode(string path, DateTime written, int loadNumber)
    {
        var name = Path.GetFileName(path);
        var timer = Stopwatch.StartNew();

        // Off the main thread: an MP3's length is found by reading through the whole file.
        var opening = Task.Run(() => AudioDecoder.Open(path, MonoAboveSamples));
        while (!opening.IsCompleted) yield return null;

        if (loadNumber != _loadNumber)
        {
            if (opening.IsCompletedSuccessfully) opening.Result.Dispose();
            yield break;
        }

        if (opening.IsFaulted)
        {
            DecodeFailed(name, opening.Exception!.GetBaseException().Message);
            yield break;
        }

        var song = opening.Result;
        AudioClip clip;
        try
        {
            // MP3 lengths are estimated, so a second of room is left at the end.
            clip = AudioClip.Create(Path.GetFileNameWithoutExtension(path), (int)Math.Min(int.MaxValue, song.Frames + song.SampleRate), song.Channels, song.SampleRate, false);
            clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
        }
        catch (Exception e)
        {
            song.Dispose();
            DecodeFailed(name, e.Message);
            yield break;
        }

        var chunks = new BlockingCollection<float[]>(ChunksDecodedAhead);
        var decoding = Task.Run(() =>
        {
            using (song)
            {
                try
                {
                    var block = new float[song.SampleRate * song.Channels];
                    int read;
                    while ((read = song.Read(block, 0, block.Length)) > 0) chunks.Add(block[..read]);
                }
                finally
                {
                    chunks.CompleteAdding();
                }
            }
        });

        long framesCopied = 0;
        while (!chunks.IsCompleted || !decoding.IsCompleted)
        {
            if (loadNumber != _loadNumber)
            {
                // Its next Add throws, which ends the decoding thread.
                chunks.CompleteAdding();
                UnityEngine.Object.Destroy(clip);
                yield break;
            }

            for (var copied = 0; copied < ChunksCopiedPerFrame && chunks.TryTake(out var chunk); copied++)
            {
                var frames = Math.Min(chunk.Length / song.Channels, clip.samples - framesCopied);
                if (frames <= 0) continue;

                try
                {
                    clip.SetData(frames * song.Channels == chunk.Length ? chunk : chunk[..(int)(frames * song.Channels)], (int)framesCopied);
                }
                catch (Exception e)
                {
                    chunks.CompleteAdding();
                    UnityEngine.Object.Destroy(clip);
                    DecodeFailed(name, e.Message);
                    yield break;
                }

                framesCopied += frames;
            }

            Progress = song.Frames > 0 ? Math.Min(1f, framesCopied / (float)song.Frames) : 0f;
            yield return null;
        }

        if (loadNumber != _loadNumber || decoding.IsFaulted)
        {
            UnityEngine.Object.Destroy(clip);
            if (decoding.IsFaulted) DecodeFailed(name, decoding.Exception!.GetBaseException().Message);
            yield break;
        }

        _clip = clip;
        Progress = 1f;
        Loading = false;
        if (_lastDecoded is { } previous && previous.Clip != null && previous.Clip != clip) UnityEngine.Object.Destroy(previous.Clip);
        _lastDecoded = (path, written, clip);
        Logger.Debug($"[Music.Loaded] {name}: {framesCopied / (double)song.SampleRate:0.0}s, {song.Channels} ch, {song.SampleRate} Hz, decoded in {timer.Elapsed.TotalSeconds:0.0}s");
    }

    private void DecodeFailed(string name, string why)
    {
        Loading = false;
        Logger.Error($"[Music.Failed] {name}: {why} - playing without music");
    }

    private bool EnsureSource()
    {
        if (_source != null) return true;

        try
        {
            var holder = new GameObject("Basalt_Music");
            UnityEngine.Object.DontDestroyOnLoad(holder);
            _source = holder.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 0f;
            _source.priority = 0;

            // "Music" exactly when there is one: "Environmental music" is the maps' ambience.
            var mixerGroups = Resources.FindObjectsOfTypeAll<AudioMixerGroup>().Where(g => g != null).ToList();
            var musicGroup = mixerGroups.FirstOrDefault(g => string.Equals(g.name, "Music", StringComparison.OrdinalIgnoreCase))
                             ?? mixerGroups.FirstOrDefault(g => g.name.Contains("Music", StringComparison.OrdinalIgnoreCase));
            if (musicGroup != null) _source.outputAudioMixerGroup = musicGroup;
            Logger.Debug($"[Music.Output] {(musicGroup == null ? "straight to the output" : $"through mixer group {musicGroup.name}")}");
            return true;
        }
        catch (Exception e)
        {
            Logger.Error($"[Music.Failed] no audio source: {e.Message}");
            _source = null;
            return false;
        }
    }

    private IEnumerator Fade(AudioSource source, int loadNumber)
    {
        var startVolume = source.volume;
        for (var elapsed = 0f; elapsed < FadeSeconds; elapsed += Time.deltaTime)
        {
            if (loadNumber != _loadNumber || source == null) yield break;
            source.volume = Mathf.Lerp(startVolume, 0f, elapsed / FadeSeconds);
            yield return null;
        }

        if (loadNumber == _loadNumber && source != null) source.Stop();
    }
}
