using BossBattles.Core;
using BossBattles.Services.Clips;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;
using UnityEngine;

namespace BossBattles.Actors;

/// <summary>
/// Records you as a rig-local <see cref="Clip"/>: head and hands inside your rig and your two
/// sticks, so it plays back anywhere, facing any way, as the actor's real gestures.
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
        Mod.Say("[Clips] recording - Record clip again to stop and save");
    }

    /// <summary>Stops; null if nothing was recorded.</summary>
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
            ActorRig.LocalPose(rig.Offset),
            ActorRig.LocalPose(rig.Head),
            ActorRig.LocalPose(rig.LeftHand),
            ActorRig.LocalPose(rig.RightHand),
            ActorRig.Stick(you.PlayerMovement.currentRawWalkInput),
            ActorRig.Stick(you.PlayerMovement.currentRawTurnInput)));
    }
}
