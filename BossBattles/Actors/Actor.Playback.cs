using BossBattles.Core;
using BossBattles.Services.Charts;
using BossBattles.Services.Clips;
using UnityEngine;
using Stack = Il2CppRUMBLE.MoveSystem.Stack;

namespace BossBattles.Actors;

/// <summary>What drives the actor: a held pose, a clip, a recorded take, or a move run by name.</summary>
internal sealed partial class Actor
{
    private Clip? _clip;
    private float _clipStartedAt;
    private Take? _take;

    /// <summary>The worst distance the actor was found from a take's path, once following.</summary>
    public float WorstTakePathError { get; private set; }

    public float LastTakePathError { get; private set; }

    /// <summary>The worst turn needed to keep the actor to a take's facing, once following.</summary>
    public float WorstTakeFacingError { get; private set; }

    public int TakeMovesPlayed { get; private set; }

    public bool PlayingTake => Exists && _take != null;

    /// <summary>The head-and-hands pose held under bot control when nothing is playing.</summary>
    public void SetPose(ActorPose pose) => _pose = pose;

    /// <summary>
    /// Plays a clip through the actor's headset, controllers and sticks, so the game recognises the
    /// gestures and runs the moves itself. Bot control only.
    /// </summary>
    public void PlayClip(Clip clip)
    {
        if (!IsBotControlled || clip.Frames.Count == 0) return;

        _clip = clip;
        _clipStartedAt = Time.time;
        Mod.Note("Actor.Clip", $"{this}: playing \"{clip.Name}\" ({clip.Duration:0.0}s, {clip.Frames.Count} frames)", echo: false);
    }

    public void StopClip()
    {
        _clip = null;
        Input.Clear();
    }

    /// <summary>
    /// Performs a recorded take exactly: its poses drive the rig, its moves run at their recorded
    /// times with gesture recognition off (so nothing fires twice), and the actor is held to the
    /// take's path in <paramref name="lane"/>. Plays from <paramref name="from"/> until chart time
    /// <paramref name="until"/> or the clip's end, timed by the chart's <paramref name="clock"/>.
    /// A mimed take runs none of its moves.
    /// </summary>
    public void PlayTake(Clip clip, double from, double? until, LaneFrame lane, Func<double> clock, bool mime = false)
    {
        if (!IsBotControlled || clip.Frames.Count == 0) return;

        EndTake();
        StopClip();

        var nextMove = 0;
        while (nextMove < clip.Moves.Count && clip.Moves[nextMove].Time < from) nextMove++;
        _take = new Take(clip, from, clock(), until, lane, clock) { NextMove = nextMove, Mime = mime };

        try
        {
            _player!.Controller.PlayerProcessor.RemoveAllStackInputSources();
        }
        catch (Exception e)
        {
            Mod.Note("Actor.TakeRecognitionOn", $"{this}: could not turn gesture recognition off - moves may fire twice: {e.Message}");
        }

        Mod.Note("Actor.Take", $"{this}: \"{clip.Name}\" from {from:0.00}s{(until is { } u ? $" until {u:0.00}s" : "")}, {(mime ? "mimed" : $"{clip.Moves.Count - nextMove} move(s)")}", echo: false);
    }

    /// <summary>Stops a take and gives the actor its gesture recognition back.</summary>
    public void EndTake()
    {
        if (_take == null) return;
        _take = null;
        Input.Clear();

        if (!Exists) return;
        try
        {
            _player!.Controller.PlayerProcessor.InitializeStackInputSources(true);
        }
        catch (Exception e)
        {
            Mod.Note("Actor.TakeRecognitionOff", $"{this}: gesture recognition not restored: {e.Message}");
        }
    }

    /// <summary>Runs one of the actor's moves by its stack name (e.g. "Straight"), no gesture involved.</summary>
    public bool RunMove(string name)
    {
        if (!Exists) return false;

        Stack? stack = _player!.Controller.PlayerProcessor.availableStacks.ToArray()
            .FirstOrDefault(s => string.Equals(s.CachedName, name, StringComparison.OrdinalIgnoreCase));

        if (stack == null)
        {
            Mod.Note("Actor.UnknownMove", $"{this} has no move called {name}");
            return false;
        }

        _player.Controller.PlayerProcessor.Execute(stack);
        Mod.Note("Actor.Move", $"{this}: {stack.CachedName}", echo: false);
        return true;
    }

    private void PlayClipFrame()
    {
        var t = Time.time - _clipStartedAt;
        if (t > _clip!.Duration)
        {
            StopClip();
            _pose?.ApplyTo(_rig!);
            return;
        }

        ClipFrame frame = _clip.Sample(t);
        ApplyClipPose(frame);
        Input.Walk = new Vector2(frame.Walk.X, frame.Walk.Y);
        Input.Turn = new Vector2(frame.Turn.X, frame.Turn.Y);
    }

    private void ApplyClipPose(ClipFrame frame)
    {
        Apply(_rig!.Offset, frame.Offset);
        Apply(_rig.Head, frame.Head);
        Apply(_rig.LeftHand, frame.LeftHand);
        Apply(_rig.RightHand, frame.RightHand);

        static void Apply(Transform part, ClipPose pose)
        {
            part.SetLocalPositionAndRotation(
                new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z),
                new Quaternion(pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W));
        }
    }

    private void PlayTakeFrame()
    {
        Take take = _take!;
        var now = take.Clock();
        var clipTime = take.From + (now - take.StartedAt);

        if ((take.Until is { } until && now >= until) || clipTime > take.Clip.Duration)
        {
            EndTake();
            _pose?.ApplyTo(_rig!);
            return;
        }

        ClipFrame frame = take.Clip.Sample((float)clipTime);
        ApplyClipPose(frame);

        // The path and facing are held below; replaying the sticks as well would move it twice.
        Input.Walk = Vector2.zero;
        Input.Turn = frame.Yaw == null ? new Vector2(frame.Turn.X, frame.Turn.Y) : Vector2.zero;

        while (take.NextMove < take.Clip.Moves.Count && take.Clip.Moves[take.NextMove].Time <= clipTime)
        {
            var move = take.Clip.Moves[take.NextMove].Move;
            take.NextMove++;
            if (!take.Mime && RunMove(move)) TakeMovesPlayed++;
        }

        if (frame.Lane is { } spot) FollowPath(take.Lane, spot);
        if (frame.Yaw is { } yaw) FollowFacing(take.Lane, yaw);
    }

    /// <summary>
    /// Shifts both physics bodies so the floor under the actor's head is where the performer stood;
    /// moving the bodies works whatever the plant constraints, and keeps a jump's height.
    /// </summary>
    private void FollowPath(LaneFrame lane, System.Numerics.Vector2 spot)
    {
        Vector3 wanted = Describe.ToUnity(lane.ToWorld(new System.Numerics.Vector3(spot.X, 0f, spot.Y)));
        Vector3 actual = _rig!.FloorPoint;
        var shift = new Vector3(wanted.x - actual.x, 0f, wanted.z - actual.z);

        // The first half second snaps the actor from where it was placed; only later errors count.
        var error = shift.magnitude;
        LastTakePathError = error;
        if (Following) WorstTakePathError = Math.Max(WorstTakePathError, error);
        if (error < 0.01f) return;

        // A big gap (a section joining another take) is closed over a few frames rather than in one jump.
        if (error > 0.25f) shift *= Mathf.Clamp01(Time.deltaTime * 10f);

        foreach (Rigidbody? body in PlantedBodies())
        {
            body?.position += shift;
        }
    }

    /// <summary>
    /// Turns the actor to the performer's facing with the game's own rotation; replaying the turn
    /// stick alone drifted (snap turns trigger on thresholds, smooth turns on frame time).
    /// </summary>
    private void FollowFacing(LaneFrame lane, float laneYaw)
    {
        var wanted = lane.ToWorldYaw(laneYaw);
        var angle = Mathf.DeltaAngle(_rig!.Vr.eulerAngles.y, wanted);
        if (Mathf.Abs(angle) < 1f) return;

        _player!.Controller.PlayerMovement.Rotate(angle, false);
        if (Following) WorstTakeFacingError = Math.Max(WorstTakeFacingError, Mathf.Abs(angle));
    }

    private bool Following => _take != null && _take.Clock() - _take.StartedAt > 0.5;

    private sealed record Take(Clip Clip, double From, double StartedAt, double? Until, LaneFrame Lane, Func<double> Clock)
    {
        public int NextMove { get; set; }

        public bool Mime { get; init; }
    }
}
