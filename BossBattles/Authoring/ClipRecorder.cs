using System.Numerics;
using BossBattles.Actors;
using BossBattles.Services.Charts;
using BossBattles.Services.Clips;
using Il2CppRUMBLE.Input;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;

namespace BossBattles.Authoring;

/// <summary>
/// Records a player as a clip: head, hands and fingers, where they stand and face in a lane, and every move the
/// game recognises from them. Clip time starts at 0 with the first <see cref="Update"/>; where it plays is the
/// track's business. Moves come from the player's stack processor's <c>OnStackExecutionStart</c>; the converted
/// delegate is kept so leaving removes the same object.
/// </summary>
internal sealed class ClipRecorder
{
    private const float SampleRate = 30f;

    private readonly List<ClipFrame> _frames = [];
    private readonly List<ClipMove> _moves = [];
    private Il2CppSystem.Action<StackConfiguration>? _onMove;
    private PlayerStackProcessor? _processor;
    private PlayerController? _subject;
    private Anchor _anchor;
    private double? _startTime;
    private double _clipTime;
    private double _nextSample;

    public bool Recording { get; private set; }

    public double Length => _frames.Count == 0 ? 0 : _frames[^1].Time;

    public int MoveCount => _moves.Count;

    public void Start(PlayerController subject, Anchor space)
    {
        Cancel();
        _subject = subject;
        _anchor = space;

        try
        {
            _processor = subject.PlayerProcessor;
            _onMove ??= new Action<StackConfiguration>(OnMove);
            _processor.OnStackExecutionStart = Il2CppSystem.Delegate.Combine(_processor.OnStackExecutionStart, _onMove).Cast<Il2CppSystem.Action<StackConfiguration>>();
        }
        catch (Exception e)
        {
            _processor = null;
            Logger.Warning($"[Clip.NoMoveEvents] moves cannot be recorded, only poses: {e.Message}");
        }

        Recording = true;
        Logger.Debug("[Clip.Recording] started");
    }

    public Clip? Stop(string name)
    {
        if (!Recording) return null;

        Clip? clip = _frames.Count < 2 ? null : new Clip(name, _frames.ToList()) { Moves = _moves.ToList() };
        Cancel();

        if (clip == null)
        {
            Logger.Info("[Clip.Empty] nothing was recorded");
            return null;
        }

        Logger.Debug($"[Clip.Recorded] {name}: {clip.Duration:0.00}s, {clip.Frames.Count} frames, {clip.Moves.Count} moves ({string.Join(", ", clip.Moves.Select(m => m.Move))})");
        return clip;
    }

    /// <summary>Samples the subject; <paramref name="time"/> is any clock in seconds, and its first reading is clip time 0.</summary>
    public void Update(double time)
    {
        if (!Recording || _subject == null) return;

        _startTime ??= time;
        _clipTime = time - _startTime.Value;
        if (_clipTime < _nextSample) return;
        _nextSample = _clipTime + (1.0 / SampleRate);

        var rig = ActorRig.From(_subject);
        PlayerHandPresence hands = _subject.PlayerHandPresence;
        Vector3 spot = _anchor.ToLocal(StringHelpers.ToNumerics(rig.Feet));
        _frames.Add(new ClipFrame(
            (float)_clipTime,
            ActorRig.LocalPose(rig.Head),
            ActorRig.LocalPose(rig.LeftHand),
            ActorRig.LocalPose(rig.RightHand),
            new Vector2(spot.X, spot.Z),
            _anchor.ToLocalYaw(rig.Vr.eulerAngles.y),
            ActorRig.Fingers(hands.GetHandPresenceInputForHand(InputManager.Hand.Left, false)),
            ActorRig.Fingers(hands.GetHandPresenceInputForHand(InputManager.Hand.Right, false))));
    }

    /// <summary>Abandons a recording without a clip.</summary>
    public void Cancel()
    {
        Recording = false;
        LeaveMoveEvents();
        _frames.Clear();
        _moves.Clear();
        _subject = null;
        _startTime = null;
        _clipTime = 0;
        _nextSample = 0;
    }

    /// <summary>A move, at the clip time of the latest <see cref="Update"/>.</summary>
    private void OnMove(StackConfiguration config)
    {
        try
        {
            var name = config?.ExecutedStack?.CachedName;
            if (!Recording || string.IsNullOrEmpty(name)) return;

            _moves.Add(new ClipMove((float)_clipTime, name));
            Logger.Debug($"[Clip.Move] {name} at {_clipTime:0.00}s");
        }
        catch (Exception e)
        {
            Logger.Error($"[Clip.MoveFailed] {e.Message}");
        }
    }

    private void LeaveMoveEvents()
    {
        if (_processor == null || _onMove == null) return;

        try
        {
            _processor.OnStackExecutionStart = Il2CppSystem.Delegate.Remove(_processor.OnStackExecutionStart, _onMove)?.Cast<Il2CppSystem.Action<StackConfiguration>>();
        }
        catch (Exception e)
        {
            Logger.Warning($"[Clip.NoMoveEvents] could not leave the move event: {e.Message}");
        }

        _processor = null;
    }
}
