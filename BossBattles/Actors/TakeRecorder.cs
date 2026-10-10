using BossBattles.Core;
using BossBattles.Services.Charts;
using BossBattles.Services.Clips;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;
using UnityEngine;

namespace BossBattles.Actors;

/// <summary>
/// Records you as a take for an actor: what <see cref="ClipRecorder"/> records - head-and-hands
/// poses inside your rig and your sticks - plus where you stand in the lane and every move the game
/// recognises from you, all on the chart's clock, so the take lines up with the chart (and its song).
/// <para>
/// Moves are observed, not intercepted: your stack processor's own <c>OnStackExecutionStart</c>
/// event is joined for the take and left afterwards (the converted delegate is kept, so leaving
/// removes the same object that was added).
/// </para>
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

    /// <summary>Seconds recorded so far, on the chart's clock.</summary>
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

    /// <summary>Stops, and returns the take - or null when nothing was recorded.</summary>
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

    /// <summary>Called each frame while recording.</summary>
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
            Pose(rig.Offset),
            Pose(rig.Head),
            Pose(rig.LeftHand),
            Pose(rig.RightHand),
            Stick(you.PlayerMovement.currentRawWalkInput),
            Stick(you.PlayerMovement.currentRawTurnInput),
            new System.Numerics.Vector2(lane.X, lane.Z),
            _lane.ToLaneYaw(rig.Vr.eulerAngles.y)));
    }

    /// <summary>Abandons a recording (a scene change, a stop elsewhere) without a take.</summary>
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

    private static ClipPose Pose(Transform part) => new(
        new System.Numerics.Vector3(part.localPosition.x, part.localPosition.y, part.localPosition.z),
        new System.Numerics.Quaternion(part.localRotation.x, part.localRotation.y, part.localRotation.z, part.localRotation.w));

    private static System.Numerics.Vector2 Stick(Vector2 stick) => new(stick.x, stick.y);
}
