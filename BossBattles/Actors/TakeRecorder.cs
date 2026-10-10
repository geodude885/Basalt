using BossBattles.Core;
using BossBattles.Services.Charts;
using BossBattles.Services.Clips;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;

namespace BossBattles.Actors;

/// <summary>
/// Records you as a take for an actor: a clip plus where you stand in the lane and every move the
/// game recognises from you, on the chart's clock. Moves come from your stack processor's
/// <c>OnStackExecutionStart</c>; the converted delegate is kept so leaving removes the same object.
/// </summary>
internal sealed class TakeRecorder
{
    private const float SampleRate = 30f;

    private readonly List<ClipFrame> _frames = [];
    private readonly List<ClipMove> _moves = [];
    private Il2CppSystem.Action<StackConfiguration>? _onMove;
    private PlayerStackProcessor? _processor;
    private Func<double>? _clock;
    private LaneFrame _lane;
    private double _nextSample;

    public bool Recording { get; private set; }

    public double Length => _frames.Count == 0 ? 0 : _frames[^1].Time - _frames[0].Time;

    public int MoveCount => _moves.Count;

    public void Start(PlayerController you, LaneFrame lane, Func<double> clock)
    {
        _frames.Clear();
        _moves.Clear();
        _lane = lane;
        _clock = clock;
        _nextSample = double.NegativeInfinity;

        try
        {
            _processor = you.PlayerProcessor;
            _onMove ??= new Action<StackConfiguration>(OnMove);
            _processor.OnStackExecutionStart = Il2CppSystem.Delegate.Combine(_processor.OnStackExecutionStart, _onMove).Cast<Il2CppSystem.Action<StackConfiguration>>();
        }
        catch (Exception e)
        {
            _processor = null;
            Mod.Note("Take.NoMoveEvents", $"moves cannot be recorded, only poses: {e.Message}");
        }

        Recording = true;
        Mod.Note("Take.Recording", "started", echo: false);
    }

    /// <summary>Stops; null if nothing was recorded.</summary>
    public Clip? Stop(string name)
    {
        if (!Recording) return null;
        Recording = false;
        LeaveMoveEvents();

        if (_frames.Count < 2)
        {
            Mod.Note("Take.Empty", "nothing was recorded");
            return null;
        }

        var take = new Clip(name, _frames.ToList()) { Moves = _moves.ToList() };
        Mod.Note("Take.Recorded", $"{name}: {take.Start:0.00}-{take.Duration:0.00}s, {take.Frames.Count} frames, {take.Moves.Count} moves ({string.Join(", ", take.Moves.Select(m => m.Move))})", echo: false);
        return take;
    }

    public void Update()
    {
        if (!Recording || _clock == null) return;

        var now = _clock();
        if (now < 0 || now < _nextSample) return;
        _nextSample = Math.Max(_nextSample, now) + (1.0 / SampleRate);

        PlayerController? you = PlayerManager.instance?.localPlayer?.Controller;
        if (you == null) return;

        var rig = ActorRig.Of(you);
        System.Numerics.Vector3 lane = _lane.ToLane(Describe.ToNumerics(rig.FloorPoint));
        _frames.Add(new ClipFrame(
            (float)now,
            ActorRig.LocalPose(rig.Offset),
            ActorRig.LocalPose(rig.Head),
            ActorRig.LocalPose(rig.LeftHand),
            ActorRig.LocalPose(rig.RightHand),
            ActorRig.Stick(you.PlayerMovement.currentRawWalkInput),
            ActorRig.Stick(you.PlayerMovement.currentRawTurnInput),
            new System.Numerics.Vector2(lane.X, lane.Z),
            _lane.ToLaneYaw(rig.Vr.eulerAngles.y)));
    }

    /// <summary>Abandons a recording without a take.</summary>
    public void Cancel()
    {
        Recording = false;
        LeaveMoveEvents();
        _frames.Clear();
        _moves.Clear();
    }

    private void OnMove(StackConfiguration config)
    {
        try
        {
            if (!Recording || _clock == null) return;

            var name = config?.ExecutedStack?.CachedName;
            if (string.IsNullOrEmpty(name)) return;

            _moves.Add(new ClipMove((float)_clock(), name));
            Mod.Note("Take.Move", $"{name} at {_clock():0.00}s", echo: false);
        }
        catch (Exception e)
        {
            Mod.Note("Take.MoveFailed", e.Message);
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
            Mod.Note("Take.NoMoveEvents", $"could not leave the move event: {e.Message}");
        }

        _processor = null;
    }
}
