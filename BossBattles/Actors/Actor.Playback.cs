using BossBattles.Services.Charts;
using BossBattles.Services.Clips;
using Il2CppRUMBLE.Input;
using UnityEngine;

namespace BossBattles.Actors;

internal partial class Actor
{
    private Playing? _clip;
    private (ClipFingers Left, ClipFingers Right)? _fingers;

    public bool PlayingClip => Exists && _clip != null;

    public ClipFingers Fingers(InputManager.Hand hand)
        => _fingers is { } fingerPosition ? (hand == InputManager.Hand.Left ? fingerPosition.Left : fingerPosition.Right) : default;

    public void SetPose(ActorPose pose) => _pose = pose;

    /// <summary>Plays <paramref name="clip"/> from clip time <paramref name="from"/> to <paramref name="to"/>, or its end.</summary>
    public void PlayClip(Clip clip, double from, double? to, Anchor origin, Func<double> clock, bool mime = false)
    {
        if (!Exists || clip.Frames.Count == 0) return;

        EndClip();

        var nextMove = 0;
        while (nextMove < clip.Moves.Count && clip.Moves[nextMove].Time < from) nextMove++;
        _clip = new Playing(clip, from, clock(), to, origin, clock) { NextMove = nextMove, Mime = mime };

        try
        {
            _player!.Controller.PlayerProcessor.RemoveAllStackInputSources();
        }
        catch (Exception e)
        {
            Logger.Warning($"[Actor.RecognitionNotOff] {this}: could not turn gesture recognition off - moves may fire twice: {e.Message}");
        }

        Logger.Debug($"[Actor.Clip] {this}: \"{clip.Name}\" from {from:0.00}s{(to is { } t ? $" to {t:0.00}s" : "")}, {(mime ? "mimed" : $"{clip.Moves.Count - nextMove} move(s)")}");
    }

    public void EndClip()
    {
        if (_clip == null) return;
        _clip = null;
        _fingers = null;

        if (!Exists) return;
        try
        {
            _player!.Controller.PlayerProcessor.InitializeStackInputSources(true);
        }
        catch (Exception e)
        {
            Logger.Warning($"[Actor.RecognitionNotRestored] {this}: gesture recognition not restored: {e.Message}");
        }
    }

    /// <summary>Runs actor's moves by its stack name (e.g. "Straight"), no gesture.</summary>
    public bool RunMove(string name)
    {
        if (!Exists) return false;

        var stack = _player!.Controller.PlayerProcessor.availableStacks.ToArray()
            .FirstOrDefault(s => string.Equals(s.CachedName, name, StringComparison.OrdinalIgnoreCase));

        if (stack == null)
        {
            Logger.Warning($"[Actor.UnknownMove] {this} has no move called {name}");
            return false;
        }

        _player.Controller.PlayerProcessor.Execute(stack);
        Logger.Debug($"[Actor.Move] {this}: {stack.CachedName}");
        return true;
    }

    private void ApplyClipPose(ClipFrame frame)
    {
        Apply(_rig!.Head, frame.Head);
        Apply(_rig.LeftHand, frame.LeftHand);
        Apply(_rig.RightHand, frame.RightHand);

        static void Apply(Transform part, ClipPose pose)
        {
            part.SetLocalPositionAndRotation(
                new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z),
                new Quaternion(pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W));
        }
    }

    private void PlayClipFrame()
    {
        var playing = _clip!;
        var now = playing.Clock();
        var clipTime = playing.From + (now - playing.StartedAt);

        if (clipTime >= (playing.To ?? playing.Clip.Duration))
        {
            EndClip();
            _pose?.ApplyTo(_rig!);
            return;
        }

        var frame = playing.Clip.Sample((float)clipTime);
        ApplyClipPose(frame);
        _fingers = frame.LeftFingers is { } left && frame.RightFingers is { } right ? (left, right) : null;

        while (playing.NextMove < playing.Clip.Moves.Count && playing.Clip.Moves[playing.NextMove].Time <= clipTime)
        {
            var move = playing.Clip.Moves[playing.NextMove].Move;
            playing.NextMove++;
            if (!playing.Mime) RunMove(move);
        }

        if (frame.Offset is { } offset) MoveTowards(playing.Lane, offset);
        if (frame.Yaw is { } yaw) TurnTowards(playing.Lane, yaw);
    }

    private void MoveTowards(Anchor origin, System.Numerics.Vector2 spot)
    {
        var wanted = origin.ToWorld(new System.Numerics.Vector3(spot.X, 0f, spot.Y)).ToUnity();
        var actual = _rig!.Feet;
        var shift = new Vector3(wanted.x - actual.x, 0f, wanted.z - actual.z);

        var error = shift.magnitude;

        // Ignore if already there
        if (error < 0.01f) return;

        // Dont move too fast if far away
        if (error > 0.25f) shift *= Mathf.Clamp01(Time.deltaTime * 10f);

        foreach (var body in PlantedBodies())
        {
            body?.position += shift;
        }
    }

    private void TurnTowards(Anchor origin, float laneYaw)
    {
        var wanted = origin.ToWorldYaw(laneYaw);
        var angle = Mathf.DeltaAngle(_rig!.Vr.eulerAngles.y, wanted);
        if (Mathf.Abs(angle) < 1f) return;

        _player!.Controller.PlayerMovement.Rotate(angle, false);
    }

    private record Playing(Clip Clip, double From, double StartedAt, double? To, Anchor Lane, Func<double> Clock)
    {
        public int NextMove { get; set; }

        public bool Mime { get; init; }
    }
}
