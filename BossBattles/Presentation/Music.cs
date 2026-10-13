using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using BossBattles.Services.Audio;
using MelonLoader;
using UnityEngine;
using UnityEngine.Audio;

namespace BossBattles.Presentation;

internal enum MusicState
{
    None,
    Loading,
    Ready,
    Failed,
}

/// <summary>
/// A chart's song, played 2D through the game's Music mixer group so your music volume applies.
/// <para>
/// The game ships without Unity's audio decoding, so songs are decoded in managed code on a
/// background thread and handed to the clip a second at a time; long songs are kept in mono, and
/// the last song decoded is kept until another loads.
/// </para>
/// <para>
/// The song is the battle's clock: started with <c>PlayScheduled</c> and read back from the same
/// audio clock, so frame hitches cannot drift the chart off the beat.
/// </para>
/// </summary>
internal sealed class Music
{
    private const double ScheduleAheadSeconds = 0.15;
    private const float FadeSeconds = 1.5f;

    /// <summary>About six minutes of stereo at 44.1 kHz; longer songs are kept in mono.</summary>
    private const long MonoAbove = 32_000_000;

    /// <summary>Main-thread work per frame: a few seconds of audio handed to the clip.</summary>
    private const int ChunksPerFrame = 4;

    /// <summary>The last song decoded: playing a chart again starts at once, and only one song is held at a time.</summary>
    private static (string Path, DateTime Written, AudioClip Clip)? _cached;

    private GameObject? _holder;
    private AudioSource? _source;
    private AudioClip? _clip;
    private double _dspStart;
    private bool _started;
    private int _load;

    public MusicState State { get; private set; } = MusicState.None;

    /// <summary>How much of a loading song is ready, 0 to 1.</summary>
    public float Progress { get; private set; }

    /// <summary>Full volume is this much of the clip's own level: chart songs are mastered loud.</summary>
    public const float Headroom = 0.7f;

    /// <summary>Your music volume; the chart's own multiplies it.</summary>
    public float Volume { get; set; } = 1f;

    public string? Name => _clip?.name;

    public bool Ended => _started && _clip != null && AudioSettings.dspTime - _dspStart > _clip.length + 0.5;

    /// <summary>Seconds since the song started (negative just before); null with no song playing.</summary>
    public double? Clock => _started ? AudioSettings.dspTime - _dspStart : null;

    /// <summary>Starts loading a song; <see cref="State"/> says when it is ready.</summary>
    public void Load(string path)
    {
        Stop();
        _clip = null;
        Progress = 0f;
        var load = ++_load;

        if (!File.Exists(path))
        {
            State = MusicState.Failed;
            Logger.Warning($"[Music.Missing] no song at {path} - playing without music");
            return;
        }

        DateTime written = File.GetLastWriteTimeUtc(path);
        if (_cached is { } cached && string.Equals(cached.Path, path, StringComparison.OrdinalIgnoreCase) && cached.Written == written && cached.Clip != null)
        {
            _clip = cached.Clip;
            Progress = 1f;
            State = MusicState.Ready;
            Logger.Debug($"[Music.Loaded] {Path.GetFileName(path)}: already decoded");
            return;
        }

        State = MusicState.Loading;
        MelonCoroutines.Start(Decode(path, written, load));
    }

    /// <summary>Plays the loaded song from <paramref name="start"/> seconds in; <see cref="Clock"/> runs from then.</summary>
    public void Play(double start, float chartVolume)
    {
        if (State != MusicState.Ready || _clip == null || !EnsureSource()) return;

        _source!.Stop();
        _source.clip = _clip;
        _source.volume = Mathf.Clamp01(Volume * chartVolume * Headroom);
        _source.time = (float)Math.Clamp(start, 0, Math.Max(0, _clip.length - 0.01));
        _dspStart = AudioSettings.dspTime + ScheduleAheadSeconds;
        _source.PlayScheduled(_dspStart);
        _started = true;
        Logger.Debug($"[Music.Playing] {_clip.name} from {start:0.00}s at volume {_source.volume:0.00}");
    }

    /// <summary>Fades out; the clock keeps running until <see cref="Stop"/>.</summary>
    public void FadeOut()
    {
        if (_source != null && _source.isPlaying) MelonCoroutines.Start(Fade(_source, _load));
    }

    public void Stop()
    {
        _source?.Stop();
        _started = false;
    }

    /// <summary>Forgets the song, dropping any load in flight.</summary>
    public void Clear()
    {
        Stop();
        _load++;
        _clip = null;
        Progress = 0f;
        State = MusicState.None;
    }

    private sealed class Decoding
    {
        public readonly ConcurrentQueue<float[]> Chunks = new();
        public volatile bool Done;
        public volatile string? Error;
        public int Channels;
        public int SampleRate;
        public long Frames;
        public volatile bool HeaderReady;
        public volatile bool Cancelled;
    }

    private IEnumerator Decode(string path, DateTime written, int load)
    {
        var job = new Decoding();
        var timer = Stopwatch.StartNew();
        var name = Path.GetFileName(path);
        Task.Run(() => DecodeOnThread(path, job));

        while (!job.HeaderReady && job.Error == null)
        {
            if (load != _load) { job.Cancelled = true; yield break; }
            yield return null;
        }

        if (job.Error != null)
        {
            Failed(name, job.Error);
            yield break;
        }

        AudioClip clip;
        try
        {
            // MP3 lengths are estimated, so a little room is left at the end.
            var frames = (int)Math.Min(int.MaxValue, job.Frames + job.SampleRate);
            clip = AudioClip.Create(Path.GetFileNameWithoutExtension(path), frames, job.Channels, job.SampleRate, false);
            clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
        }
        catch (Exception e)
        {
            job.Cancelled = true;
            Failed(name, e.Message);
            yield break;
        }

        long filled = 0;
        long capacity = clip.samples;
        while (true)
        {
            if (load != _load) { job.Cancelled = true; UnityEngine.Object.Destroy(clip); yield break; }

            for (var i = 0; i < ChunksPerFrame && job.Chunks.TryDequeue(out var chunk); i++)
            {
                var frames = chunk.Length / job.Channels;
                if (filled + frames > capacity) frames = (int)(capacity - filled);
                if (frames <= 0) continue;

                var data = frames * job.Channels == chunk.Length ? chunk : chunk[..(frames * job.Channels)];
                try
                {
                    clip.SetData(data, (int)filled);
                }
                catch (Exception e)
                {
                    job.Cancelled = true;
                    Failed(name, e.Message);
                    yield break;
                }

                filled += frames;
            }

            Progress = job.Frames > 0 ? Math.Min(1f, filled / (float)job.Frames) : 0f;
            if (job.Error != null) { Failed(name, job.Error); yield break; }
            if (job.Done && job.Chunks.IsEmpty) break;
            yield return null;
        }

        _clip = clip;
        Progress = 1f;
        State = MusicState.Ready;
        if (_cached is { } previous && previous.Clip != null && previous.Clip != clip) UnityEngine.Object.Destroy(previous.Clip);
        _cached = (path, written, clip);
        Logger.Debug($"[Music.Loaded] {name}: {filled / (double)job.SampleRate:0.0}s, {job.Channels} ch, {job.SampleRate} Hz, decoded in {timer.Elapsed.TotalSeconds:0.0}s");
    }

    /// <summary>On a background thread, so no game calls: decodes a second at a time into the queue.</summary>
    private static void DecodeOnThread(string path, Decoding job)
    {
        try
        {
            using IPcmSource source = AudioDecoder.Open(path);
            var mono = source.Channels > 1 && source.Frames * source.Channels > MonoAbove;
            job.Channels = mono ? 1 : source.Channels;
            job.SampleRate = source.SampleRate;
            job.Frames = source.Frames;
            job.HeaderReady = true;

            var block = new float[source.SampleRate * source.Channels];
            int read;
            while (!job.Cancelled && (read = source.Read(block, 0, block.Length)) > 0)
            {
                // A few seconds ahead of the main thread, not the whole song.
                while (job.Chunks.Count > 30 && !job.Cancelled) Thread.Sleep(5);
                if (!mono)
                {
                    job.Chunks.Enqueue(block[..read]);
                    continue;
                }

                var downmixed = new float[read / source.Channels];
                for (var f = 0; f < downmixed.Length; f++)
                {
                    var sum = 0f;
                    for (var c = 0; c < source.Channels; c++) sum += block[(f * source.Channels) + c];
                    downmixed[f] = sum / source.Channels;
                }

                job.Chunks.Enqueue(downmixed);
            }
        }
        catch (Exception e)
        {
            job.Error = e.Message;
        }
        finally
        {
            job.Done = true;
        }
    }

    private void Failed(string name, string why)
    {
        State = MusicState.Failed;
        Logger.Error($"[Music.Failed] {name}: {why} - playing without music");
    }

    private bool EnsureSource()
    {
        if (_source != null) return true;

        try
        {
            _holder = new GameObject("BossBattles_Music");
            UnityEngine.Object.DontDestroyOnLoad(_holder);
            _source = _holder.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 0f;
            _source.priority = 0;

            // "Music" exactly when there is one: "Environmental music" is the maps' ambience.
            var groups = Resources.FindObjectsOfTypeAll<AudioMixerGroup>().Where(g => g != null).ToList();
            AudioMixerGroup? music = groups.FirstOrDefault(g => string.Equals(g.name, "Music", StringComparison.OrdinalIgnoreCase))
                                     ?? groups.FirstOrDefault(g => g.name.Contains("Music", StringComparison.OrdinalIgnoreCase));
            if (music != null) _source.outputAudioMixerGroup = music;
            Logger.Debug($"[Music.Output] {(music == null ? "straight to the output" : $"through mixer group {music.name}")}; groups: {string.Join(", ", groups.Select(g => g.name))}");
            return true;
        }
        catch (Exception e)
        {
            Logger.Error($"[Music.Failed] no audio source: {e.Message}");
            _source = null;
            return false;
        }
    }

    private IEnumerator Fade(AudioSource source, int load)
    {
        var from = source.volume;
        for (var t = 0f; t < FadeSeconds; t += Time.deltaTime)
        {
            if (load != _load || source == null) yield break;
            source.volume = Mathf.Lerp(from, 0f, t / FadeSeconds);
            yield return null;
        }

        if (load == _load && source != null) source.Stop();
    }
}
