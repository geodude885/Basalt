using Basalt.Services.Charts;
using Basalt.Services.Clips;
using Il2CppRUMBLE.Input;
using Il2CppRUMBLE.Players;
using UnityEngine;

namespace Basalt.Actors;

// Plays actor motion from recorded clips
internal class ClipPlayer(Actor actor, PlayerController controller, PlayerRig rig, ActorPlacement footing, ActorPose restingPose)
{
    private Playback? _clip;
    private (ClipFingers Left, ClipFingers Right)? _fingers;

    public ActorPose RestingPose { get; set; } = restingPose;

    public bool Playing => _clip != null;

    public ClipFingers Fingers(InputManager.Hand hand)
        => _fingers is { } fingers ? (hand == InputManager.Hand.Left ? fingers.Left : fingers.Right) : default;

    // Leave `to` as null to play to end
    public void Play(Clip clip, double from, double? to, Anchor origin, Func<double> clock, bool mime = false)
    {
        if (clip.Frames.Count == 0) return;

        End();

        var nextMove = 0;
        while (nextMove < clip.Moves.Count && clip.Moves[nextMove].Time < from) nextMove++;
        _clip = new Playback(clip, from, clock(), to, origin, clock) { NextMove = nextMove, Mime = mime };

        try
        {
            controller.PlayerProcessor.RemoveAllStackInputSources();
        }
        catch (Exception e)
        {
            Logger.Warning($"[Actor.RecognitionNotOff] {actor}: could not turn gesture recognition off - moves may fire twice: {e.Message}");
        }

        Logger.Debug($"[Actor.Clip] {actor}: \"{clip.Name}\" from {from:0.00}s{(to is { } t ? $" to {t:0.00}s" : "")}, {(mime ? "mimed" : $"{clip.Moves.Count - nextMove} move(s)")}");
    }

    public void End()
    {
        if (_clip == null) return;
        _clip = null;
        _fingers = null;

        try
        {
            controller.PlayerProcessor.InitializeStackInputSources(true);
        }
        catch (Exception e)
        {
            Logger.Warning($"[Actor.RecognitionNotRestored] {actor}: gesture recognition not restored: {e.Message}");
        }
    }

    public void Update()
    {
        if (_clip != null) PlayFrame(_clip);
        else RestingPose.ApplyTo(rig);
    }

    private void PlayFrame(Playback playing)
    {
        var clipTime = playing.From + (playing.Clock() - playing.StartedAt);

        if (clipTime >= (playing.To ?? playing.Clip.Duration))
        {
            End();
            RestingPose.ApplyTo(rig);
            return;
        }

        var frame = playing.Clip.Sample((float)clipTime);
        ApplyPose(frame);
        _fingers = frame.LeftFingers is { } left && frame.RightFingers is { } right ? (left, right) : null;

        while (playing.NextMove < playing.Clip.Moves.Count && playing.Clip.Moves[playing.NextMove].Time <= clipTime)
        {
            var move = playing.Clip.Moves[playing.NextMove].Move;
            playing.NextMove++;
            if (!playing.Mime) actor.RunMove(move);
        }

        if (frame.Offset is { } offset) MoveTowards(playing.Lane, offset);
        if (frame.Yaw is { } yaw) TurnTowards(playing.Lane, yaw);
    }

    private void ApplyPose(ClipFrame frame)
    {
        Apply(rig.Head, frame.Head);
        Apply(rig.LeftHand, frame.LeftHand);
        Apply(rig.RightHand, frame.RightHand);

        static void Apply(Transform part, ClipPose pose)
        {
            part.SetLocalPositionAndRotation(
                new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z),
                new Quaternion(pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W));
        }
    }

    private void MoveTowards(Anchor origin, System.Numerics.Vector2 spot)
    {
        var wanted = origin.ToWorld(new System.Numerics.Vector3(spot.X, 0f, spot.Y)).ToUnity();
        var actual = rig.Feet;
        var shift = new Vector3(wanted.x - actual.x, 0f, wanted.z - actual.z);

        var error = shift.magnitude;

        // Ignore if already there
        if (error < 0.01f) return;

        // Dont move too fast if far away
        if (error > 0.25f) shift *= Mathf.Clamp01(Time.deltaTime * 10f);

        footing.Shift(shift);
    }

    private void TurnTowards(Anchor origin, float laneYaw)
    {
        var wanted = origin.ToWorldYaw(laneYaw);
        var angle = Mathf.DeltaAngle(rig.Vr.eulerAngles.y, wanted);
        if (Mathf.Abs(angle) < 1f) return;

        controller.PlayerMovement.Rotate(angle, false);
    }

    private record Playback(Clip Clip, double From, double StartedAt, double? To, Anchor Lane, Func<double> Clock)
    {
        public int NextMove { get; set; }

        public bool Mime { get; init; }
    }
}
