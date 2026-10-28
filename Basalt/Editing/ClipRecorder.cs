using System.Numerics;
using Basalt.Services.Charts;
using Basalt.Services.Clips;
using Il2CppRUMBLE.Input;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;

namespace Basalt.Editing;

// Records both player motion and moves
internal class ClipRecorder
{
    public bool Recording { get; private set; }
    public double Length => _frames.Count == 0 ? 0 : _frames[^1].Time;
    public int MoveCount => _moves.Count;

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

    public void StartRecording(PlayerController subject, Anchor space)
    {
        CancelRecording();
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

    public Clip? StopRecording(string name)
    {
        if (!Recording) return null;

        var clip = _frames.Count < 2 ? null : new Clip(name, _frames.ToList()) { Moves = _moves.ToList() };
        CancelRecording();

        if (clip == null)
        {
            Logger.Info("[Clip.Empty] nothing was recorded");
            return null;
        }

        Logger.Debug($"[Clip.Recorded] {name}: {clip.Duration:0.00}s, {clip.Frames.Count} frames, {clip.Moves.Count} moves ({string.Join(", ", clip.Moves.Select(m => m.Move))})");
        return clip;
    }

    public void Update(double time)
    {
        if (!Recording || _subject == null) return;

        _startTime ??= time;
        _clipTime = time - _startTime.Value;
        if (_clipTime < _nextSample) return;
        _nextSample = _clipTime + (1.0 / SampleRate);

        var rig = PlayerRig.From(_subject);
        var hands = _subject.PlayerHandPresence;
        var spot = _anchor.ToLocal(rig.Feet.ToNumerics());

        _frames.Add(new ClipFrame(
            (float)_clipTime,
            ClipPose.From(rig.Head),
            ClipPose.From(rig.LeftHand),
            ClipPose.From(rig.RightHand),
            new Vector2(spot.X, spot.Z),
            _anchor.ToLocalYaw(rig.Vr.eulerAngles.y),
            ClipFingers.From(hands.GetHandPresenceInputForHand(InputManager.Hand.Left, false)),
            ClipFingers.From(hands.GetHandPresenceInputForHand(InputManager.Hand.Right, false))));
    }

    public void CancelRecording()
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
