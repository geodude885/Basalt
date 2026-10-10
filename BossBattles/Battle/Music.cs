using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using BossBattles.Core;
using BossBattles.Services.Audio;
using MelonLoader;
using UnityEngine;
using UnityEngine.Audio;

namespace BossBattles.Battle;

/// <summary>Where a song is in loading.</summary>
internal enum MusicState
{
    None,
    Loading,
    Ready,
    Failed,
}

/// <summary>
/// A chart's song - MP3, OGG or WAV - played 2D through the game's Music mixer group, so the game's
/// own music volume applies.
/// <para>
/// The game ships without Unity's audio decoding, so songs are decoded in managed code
/// (<see cref="AudioDecoder"/>) on a background thread and handed to an <c>AudioClip</c> a second at a
/// time on the main thread: a long song is never held whole twice. Past
/// <see cref="MonoAbove"/> samples a song is kept in mono, which halves its memory. A decoded song is
/// kept for its file (until the file changes), so playing a chart again starts at once.
/// </para>
/// <para>
/// The song is the battle's clock: it is started on the audio clock (<c>PlayScheduled</c>) and
/// <see cref="Clock"/> reads the same clock, so frame hitches cannot drift the chart off the beat.
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

    private static readonly Dictionary<string, (DateTime Written, AudioClip Clip)> Cache = new(StringComparer.OrdinalIgnoreCase);

    private GameObject? _holder;
    private AudioSource? _source;
    private AudioClip? _clip;
    private double _dspStart;
    private bool _started;
    private int _load;

    public MusicState State { get; private set; } = MusicState.None;

    /// <summary>How much of a loading song is ready, 0 to 1.</summary>
    public float Progress { get; private set; }

    /// <summary>
    /// Full volume is this much of the clip's own level: chart songs are mastered loud, and at
    /// 100% one was painful in VR.
    /// </summary>
    public const float Headroom = 0.7f;

    /// <summary>Your music volume (the setting), times the chart's own.</summary>
    public float Volume { get; set; } = 1f;

    public string? Name => _clip?.name;

    /// <summary>Whether a started song has played through.</summary>
    public bool Ended => _started && _clip != null && AudioSettings.dspTime - _dspStart > _clip.length + 0.5;

    /// <summary>Seconds of chart time since the song was started (negative just before), or null with no song playing.</summary>
    public double? Clock => _started ? AudioSettings.dspTime - _dspStart : null;

    /// <summary>Starts loading a song file; <see cref="State"/> says when it is ready.</summary>
    public void Load(string path)
    {
        Stop();
        _clip = null;
        Progress = 0f;
        var load = ++_load;

        if (!File.Exists(path))
        {
            State = MusicState.Failed;
            Mod.Note("Music.Missing", $"no song at {path} - playing without music");
            return;
        }

        DateTime written = File.GetLastWriteTimeUtc(path);
        if (Cache.TryGetValue(path, out (DateTime Written, AudioClip Clip) cached) && cached.Written == written && cached.Clip != null)
        {
            _clip = cached.Clip;
            Progress = 1f;
            State = MusicState.Ready;
            Mod.Note("Music.Loaded", $"{Path.GetFileName(path)}: already decoded", echo: false);
            return;
        }

        State = MusicState.Loading;
        MelonCoroutines.Start(Decode(path, written, load));
    }

    /// <summary>Plays the loaded song from <paramref name="start"/> seconds in, on the audio clock; <see cref="Clock"/> runs from then.</summary>
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
        Mod.Note("Music.Playing", $"{_clip.name} from {start:0.00}s at volume {_source.volume:0.00}", echo: false);
    }

    /// <summary>Fades out (the battle ended); the clock keeps running until <see cref="Stop"/>.</summary>
    public void FadeOut()
    {
        if (_source != null && _source.isPlaying) MelonCoroutines.Start(Fade(_source, _load));
    }

    public void Stop()
    {
        _source?.Stop();
        _started = false;
    }

    /// <summary>Forgets the song (a new battle); loading in flight is dropped.</summary>
    public void Clear()
    {
        Stop();
        _load++;
        _clip = null;
        Progress = 0f;
        State = MusicState.None;
    }

    /// <summary>What the decoding thread reports to the main thread.</summary>
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

        long written_ = 0;
        long capacity = clip.samples;
        while (true)
        {
            if (load != _load) { job.Cancelled = true; UnityEngine.Object.Destroy(clip); yield break; }

            for (var i = 0; i < ChunksPerFrame && job.Chunks.TryDequeue(out var chunk); i++)
            {
                var frames = chunk.Length / job.Channels;
                if (written_ + frames > capacity) frames = (int)(capacity - written_);
                if (frames <= 0) continue;

                var data = frames * job.Channels == chunk.Length ? chunk : chunk.Take(frames * job.Channels).ToArray();
                try
                {
                    clip.SetData(data, (int)written_);
                }
                catch (Exception e)
                {
                    job.Cancelled = true;
                    Failed(name, e.Message);
                    yield break;
                }

                written_ += frames;
            }

            Progress = job.Frames > 0 ? Math.Min(1f, written_ / (float)job.Frames) : 0f;
            if (job.Error != null) { Failed(name, job.Error); yield break; }
            if (job.Done && job.Chunks.IsEmpty) break;
            yield return null;
        }

        _clip = clip;
        Progress = 1f;
        State = MusicState.Ready;
        Cache[path] = (written, clip);
        Mod.Note("Music.Loaded", $"{name}: {written_ / (double)job.SampleRate:0.0}s, {job.Channels} ch, {job.SampleRate} Hz, decoded in {timer.Elapsed.TotalSeconds:0.0}s", echo: false);
    }

    /// <summary>Background thread: decode a second at a time into the queue (pure managed code - no game calls here).</summary>
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
                // Keep the queue a few seconds ahead of the main thread, not the whole song.
                while (job.Chunks.Count > 30 && !job.Cancelled) Thread.Sleep(5);
                job.Chunks.Enqueue(mono ? Downmix(block, read, source.Channels) : block.Take(read).ToArray());
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

    private static float[] Downmix(float[] block, int count, int channels)
    {
        var mono = new float[count / channels];
        for (var f = 0; f < mono.Length; f++)
        {
            var sum = 0f;
            for (var c = 0; c < channels; c++) sum += block[(f * channels) + c];
            mono[f] = sum / channels;
        }

        return mono;
    }

    private void Failed(string name, string why)
    {
        State = MusicState.Failed;
        Mod.Note("Music.Failed", $"{name}: {why} - playing without music");
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

            // "Music" exactly when the game has it - "Environmental music" is the maps' ambience.
            var groups = Resources.FindObjectsOfTypeAll<AudioMixerGroup>().Where(g => g != null).ToList();
            AudioMixerGroup? music = groups.FirstOrDefault(g => string.Equals(g.name, "Music", StringComparison.OrdinalIgnoreCase))
                                     ?? groups.FirstOrDefault(g => g.name.Contains("Music", StringComparison.OrdinalIgnoreCase));
            if (music != null) _source.outputAudioMixerGroup = music;
            Mod.Note("Music.Output", $"{(music == null ? "straight to the output" : $"through mixer group {music.name}")}; groups: {string.Join(", ", groups.Select(g => g.name))}", echo: false);
            return true;
        }
        catch (Exception e)
        {
            Mod.Note("Music.Failed", $"no audio source: {e.Message}");
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
