using BossBattles.Core;
using BossBattles.Services.Clips;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;
using UnityEngine;

namespace BossBattles.Actors;

/// <summary>
/// Records you as a <see cref="Clip"/>: your head-and-hands poses inside your rig and your two
/// sticks, at <see cref="SampleRate"/> Hz. Rig-local, so the clip plays back anywhere and facing
/// any way. Played on an actor it goes through the same input the game reads from a headset and
/// controllers, so your gestures become the actor's real moves.
/// </summary>
internal sealed class ClipRecorder
{
    private const float SampleRate = 30f;

    private readonly List<ClipFrame> _frames = [];
    private float _startedAt;
    private float _nextSample;

    public bool Recording { get; private set; }

    public void Start()
    {
        _frames.Clear();
        _startedAt = Time.time;
        _nextSample = Time.time;
        Recording = true;
        Mod.Say("[Clips] recording - Ctrl+Shift+R again to stop and save");
    }

    /// <summary>Stops, and returns the clip - or null if nothing was recorded.</summary>
    public Clip? Stop()
    {
        Recording = false;
        if (_frames.Count < 2)
        {
            Mod.Say("[Clips] nothing recorded");
            return null;
        }

        var clip = new Clip($"clip-{DateTime.Now:yyyyMMdd-HHmmss}", _frames.ToList());
        _frames.Clear();
        return clip;
    }

    public void Update()
    {
        if (!Recording || Time.time < _nextSample) return;
        _nextSample += 1f / SampleRate;

        PlayerController? you = PlayerManager.instance?.localPlayer?.Controller;
        if (you == null) return;

        var rig = ActorRig.Of(you);
        _frames.Add(new ClipFrame(
            Time.time - _startedAt,
            Pose(rig.Offset),
            Pose(rig.Head),
            Pose(rig.LeftHand),
            Pose(rig.RightHand),
            Stick(you.PlayerMovement.currentRawWalkInput),
            Stick(you.PlayerMovement.currentRawTurnInput)));
    }

    private static ClipPose Pose(Transform part) => new(
        new System.Numerics.Vector3(part.localPosition.x, part.localPosition.y, part.localPosition.z),
        new System.Numerics.Quaternion(part.localRotation.x, part.localRotation.y, part.localRotation.z, part.localRotation.w));

    private static System.Numerics.Vector2 Stick(Vector2 stick) => new(stick.x, stick.y);
}
