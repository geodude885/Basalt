using System.Collections;
using BossBattles.Core;
using BossBattles.Services.Clips;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPhoton.Pun;
using Il2CppRUMBLE.Combat.ShiftStones;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using PlayerData = Il2CppRUMBLE.Players.PlayerData;
using Stack = Il2CppRUMBLE.MoveSystem.Stack;

namespace BossBattles.Actors;

/// <summary>How an actor's body is run.</summary>
internal enum ActorMode
{
    /// <summary>A Local player the game simulates - movement, jumps, physics states, knockback, moves.</summary>
    Simulated,

    /// <summary>PUPPET: a stripped remote player posed by the mod (ReplayMod-style). Testing only - see Actor.Puppet.cs.</summary>
    Puppet,
}

/// <summary>
/// A game-simulated player that is not you: what a battle puts in the lane.
/// <para>
/// Created as a Local player (its actor number is yours), so the game's own movement, physics,
/// moves and shiftstones run on it; drawn as a Remote one (visuals, nameplate and health bar
/// re-initialised as Remote), so it has a body, a floating health bar and sounds in the world
/// rather than in your head. Every way a Local player is "you" that has mattered is handled - see
/// CLAUDE.md, "Extra players".
/// </para>
/// </summary>
internal sealed partial class Actor
{
    private const float SpawnTimeoutSeconds = 5f;
    private const float VisualsTimeoutSeconds = 10f;
    private const float KnockOutSeconds = 3f;
    private const short MaxHealth = 20;
    private const float FacingCheckInterval = 0.25f;
    private const float FacingTolerance = 15f;
    private const float PlacementCheckSeconds = 0.5f;
    private const float PlacementTolerance = 0.5f;
    private const RigidbodyConstraints PlantConstraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ;

    private Player? _player;
    private ActorRig? _rig;
    private ActorPose? _pose;
    private bool _spawning;
    private Transform? _remoteUi;
    private Transform? _nameTag;
    private Transform? _localUi;
    private Collider[]? _ownColliders;
    private bool _planted;
    private bool _knockedOut;
    private float _nextFacingCheck;
    private Clip? _clip;
    private float _clipStartedAt;
    private Take? _take;

    public Actor(string id, string name, string look, ActorMode mode = ActorMode.Simulated, IReadOnlyList<string>? stones = null)
    {
        Id = id;
        Name = name;
        Look = look;
        Mode = mode;
        Stones = stones;
    }

    /// <summary>The stones it wears, by name; null for the ones you have equipped.</summary>
    public IReadOnlyList<string>? Stones { get; }

    public string Id { get; }

    public string Name { get; }

    /// <summary>"you", "default", "defaultFemale", "defaultMale", or a character string as the game stores it.</summary>
    public string Look { get; }

    public ActorMode Mode { get; }

    public bool Exists => _player != null && _player.Controller != null;

    /// <summary>Spawned, with its body baked and on show.</summary>
    public bool Ready { get => Exists && field; private set; }

    public bool IsSimulated => Exists && Mode == ActorMode.Simulated;

    public Player? Player => Exists ? _player : null;

    public Vector3? HeadPosition => Exists && _rig != null ? _rig.Head.position : null;

    public Vector3? FloorPosition => Exists && _rig != null ? _rig.FloorPoint : null;

    public int KnockOuts { get; private set; }

    public bool KnockedOut => Exists && _knockedOut;

    public ActorControl Control { get; private set; } = ActorControl.Bot;

    public ActorInput Input { get; } = new();

    /// <summary>The actor's sticks come from <see cref="Input"/>, not your controllers.</summary>
    public bool IsBotControlled => IsSimulated && Control == ActorControl.Bot;

    /// <summary>How much of the game's knockback it takes: 1 normal, 0 none. Planting stops every other shove.</summary>
    public float KnockbackTaken { get; set; } = 1f;

    /// <summary>Planted: nothing can move it sideways until <see cref="Unplant"/>.</summary>
    public bool Planted => Exists && _planted;

    /// <summary>Raised once the body is baked and on show.</summary>
    public event Action<Actor>? BecameReady;

    /// <summary>Raised on each knock-out (lethal damage, which the actor survives).</summary>
    public event Action<Actor>? WasKnockedOut;

    public bool Owns(Player player) => _player != null && _player.Pointer == player.Pointer;

    public override string ToString() => $"{Name} [{Id}]";

    // ---- Lifecycle -------------------------------------------------------------------------------

    /// <summary>Creates the body at <paramref name="floorPosition"/>, facing <paramref name="facing"/>. Asynchronous: see <see cref="BecameReady"/>.</summary>
    public void Spawn(Vector3 floorPosition, Quaternion facing)
    {
        if (_spawning || Exists)
        {
            Mod.Note("Actor.SpawnSkipped", $"{this}: {(_spawning ? "already spawning" : "already exists")}");
            return;
        }

        if (PlayerManager.instance?.localPlayer?.Controller == null)
        {
            Mod.Note("Actor.SpawnSkipped", $"{this}: no local player to model it on");
            return;
        }

        _spawning = true;
        MelonCoroutines.Start(SpawnRoutine(floorPosition, facing));
    }

    public void Despawn()
    {
        if (_player != null)
        {
            PlayerManager.instance?.AllPlayers?.Remove(_player);
            if (_player.Controller != null) UnityEngine.Object.Destroy(_player.Controller.gameObject);
            Mod.Note("Actor.Despawned", $"{this}: {Describe.Player(_player)}");
        }

        Forget();
    }

    /// <summary>For a scene change: the body went with the scene, but the player list entry did not.</summary>
    public void ForgetSceneObjects()
    {
        if (_player != null) PlayerManager.instance?.AllPlayers?.Remove(_player);
        Forget();
    }

    private IEnumerator SpawnRoutine(Vector3 floorPosition, Quaternion facing)
    {
        Player? player = null;
        try
        {
            player = Create(floorPosition, facing);
        }
        catch (Exception e)
        {
            Mod.Note("Actor.SpawnFailed", $"{this}: {e}");
        }

        if (player == null)
        {
            _spawning = false;
            yield break;
        }

        var deadline = Time.realtimeSinceStartup + SpawnTimeoutSeconds;
        while (player.Controller == null && Time.realtimeSinceStartup < deadline) yield return null;

        try
        {
            if (player.Controller == null) Mod.Note("Actor.SpawnFailed", $"{this}: no controller after {SpawnTimeoutSeconds}s");
            else Setup(player, floorPosition, facing);
        }
        catch (Exception e)
        {
            Mod.Note("Actor.SpawnFailed", $"{this}: {e}");
        }

        _spawning = false;
        if (Exists) MelonCoroutines.Start(FinishWhenVisible(player));
    }

    private Player Create(Vector3 floorPosition, Quaternion facing)
    {
        Player local = PlayerManager.instance.localPlayer;

        var shiftstones = new Il2CppStructArray<short>(2);
        shiftstones[0] = -1;
        shiftstones[1] = -1;

        // Your look, whatever the actor's: a Local player's PlayerVisuals.Initialize bakes and caches
        // its visuals as *the local look* (BakeApplyAndCachePlayerVisuals), so any other look here
        // would turn you into it. The actor's own look is applied later, baked as a Remote player.
        var data = new PlayerData(
            new GeneralData
            {
                // Unique per actor: baked looks are cached by this id.
                PlayFabMasterId = $"BossBattles-{Id}-{Guid.NewGuid():N}",
                PlayFabTitleId = Guid.NewGuid().ToString(),
                BattlePoints = 0,
                PublicUsername = Name,
            },
            local.Data.RedeemedMoves,
            local.Data.EconomyData,
            shiftstones,
            CopyOf(local.Data.VisualData))
        {
            PlayerMeasurement = local.Data.PlayerMeasurement,
        };

        var player = Player.CreateRemotePlayer(data);

        if (Mode == ActorMode.Simulated)
        {
            // PlayerController.Initialize types a player Local when Photon is offline, has no local
            // player, or the player's actor number matches PhotonNetwork.LocalPlayer's (ISIL).
            var actorNumber = (short)(PhotonNetwork.LocalPlayer?.ActorNumber ?? 0);
            player.AssignActorNo(actorNumber);
        }

        PlayerManager.instance.SpawnPlayerController(player, floorPosition, facing);
        return player;
    }

    private void Setup(Player player, Vector3 floorPosition, Quaternion facing)
    {
        PlayerController controller = player.Controller;
        GameObject body = controller.gameObject;
        body.name = $"BossBattles_Actor_{Id}";

        // The reset system resets or kills falling and out-of-bounds players, and a Local one
        // listens for XR recentering meant for you.
        DisableAll<PlayerResetSystem>(body);

        if (Mode == ActorMode.Simulated) SetupSimulated(body);
        else SetupPuppet(controller, body, floorPosition, facing); // PUPPET

        _player = player;
        Mod.Actors.Add(this);

        // The body is baked from VisualData only while listed in PlayerManager.AllPlayers - but a
        // second listed player stops structure-on-structure collisions registering in the Gym, so
        // the actor is listed only until its body is ready (FinishWhenVisible).
        SetInPlayerList(true);
        player.Data.VisualData = ResolveLook();

        if (Mode == ActorMode.Simulated)
        {
            PresentAsRemote(controller);
            CopyShiftstones(controller, PlayerManager.instance.localPlayer.Controller);
            _remoteUi = body.transform.Find("UI/RemoteUI");
            _nameTag = body.transform.Find("NameTag");
            _localUi = body.transform.Find("UI/LocalUI");
        }
        else
        {
            controller.PlayerVisuals.Initialize(controller); // PUPPET
        }

        _rig = ActorRig.Of(controller);
        _pose = ActorPose.Guard(ActorRig.Of(PlayerManager.instance.localPlayer.Controller));

        Mod.Note("Actor.Spawned",
            $"{this} {Mode}: type {controller.ControllerType}, look \"{Look}\", at {Describe.Vector(floorPosition)}; " +
            $"moves: {string.Join(", ", controller.PlayerProcessor.availableStacks.ToArray().Select(s => s.CachedName))}");
    }

    private IEnumerator FinishWhenVisible(Player player)
    {
        var start = Time.realtimeSinceStartup;
        while (Owns(player) && Exists && !player.Controller.PlayerVisuals.VisualsAreReadyAndApplied
               && Time.realtimeSinceStartup - start < VisualsTimeoutSeconds)
        {
            yield return null;
        }

        if (!Owns(player) || !Exists) yield break;

        try
        {
            // Your own body renderer is disabled - you are not meant to see yourself.
            SkinnedMeshRenderer bodyRenderer = player.Controller.PlayerVisuals.Renderer;
            if (bodyRenderer != null && !bodyRenderer.enabled) bodyRenderer.enabled = true;

            var baked = player.Controller.PlayerVisuals.VisualsAreReadyAndApplied;
            Mod.Note("Actor.Visuals", $"{this}: {(baked ? "baked" : "NOT baked")} after {Time.realtimeSinceStartup - start:F2}s", echo: !baked);
            SetInPlayerList(false);

            Ready = true;
            BecameReady?.Invoke(this);
        }
        catch (Exception e)
        {
            Mod.Note("Actor.VisualsFailed", $"{this}: {e}");
        }
    }

    private void SetInPlayerList(bool inList)
    {
        if (_player is null) return;
        Il2CppSystem.Collections.Generic.List<Player> players = PlayerManager.instance.AllPlayers;
        players.Remove(_player);
        if (inList) players.Add(_player);
    }

    private PlayerVisualData ResolveLook()
    {
        PlayerVisualData yours = CopyOf(PlayerManager.instance.localPlayer.Data.VisualData);

        switch (Look.Trim().ToLowerInvariant())
        {
            case "":
            case "you":
                return yours;
            case "default":
                return PlayerVisualData.Default;
            case "defaultmasc":
            case "defaultmale":
                return PlayerVisualData.DefaultMale;
            case "defaultfem":
            case "defaultfemale":
                return PlayerVisualData.DefaultFemale;
        }

        try
        {
            return PlayerVisualData.FromPlayfabDataString(Look);
        }
        catch (Exception e)
        {
            Mod.Note("Actor.LookInvalid", $"{this}: \"{Look}\" is not a look the game can read ({e.Message}) - using yours");
            return yours;
        }
    }

    private static PlayerVisualData CopyOf(PlayerVisualData look) => PlayerVisualData.FromPlayfabDataString(look.ToPlayfabDataString());

    /// <summary>
    /// A Local player's presentation-only extras that would act on you: a second camera and audio
    /// listener, haptics, LIV, the screen fade, and tracked pose drivers that read your headset.
    /// Its sounds are 2D (they would play in your ears): every source is made positional.
    /// </summary>
    private static void SetupSimulated(GameObject body)
    {
        foreach (Camera camera in body.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
        foreach (AudioListener listener in body.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
        foreach (TrackedPoseDriver driver in body.GetComponentsInChildren<TrackedPoseDriver>(true)) driver.enabled = false;
        foreach (AudioSource source in body.GetComponentsInChildren<AudioSource>(true)) source.spatialBlend = 1f;

        DisableAll<PlayerHaptics>(body);
        DisableAll<PlayerLIV>(body);
        DisableAll<PlayerCollisionFade>(body);
    }

    /// <summary>
    /// Simulated as Local, drawn as Remote: PlayerVisuals, PlayerNameTag and PlayerUI each decide in
    /// their Initialize from <c>controllerType</c> whether this is you. Flip it, re-run those three,
    /// flip it back.
    /// </summary>
    private void PresentAsRemote(PlayerController controller)
    {
        ControllerType simulatedAs = controller.controllerType;
        controller.controllerType = ControllerType.Remote;

        try
        {
            controller.PlayerVisuals.Initialize(controller);
            controller.PlayerNameTag.Initialize(controller);
            controller.PlayerUI.Initialize(controller);
        }
        catch (Exception e)
        {
            Mod.Note("Actor.PresentationFailed", $"{this}: {e}");
        }
        finally
        {
            controller.controllerType = simulatedAs;
        }
    }

    /// <summary>
    /// Equips the actor with the stones you have equipped, fresh from their pools. The flags keep
    /// it out of your save and off the network; it runs as Remote so the stones are not put on a
    /// first-person layer.
    /// </summary>
    private void CopyShiftstones(PlayerController actor, PlayerController you)
    {
        var report = new List<string>();
        ControllerType simulatedAs = actor.controllerType;
        actor.controllerType = ControllerType.Remote;

        try
        {
            // The chart's stones, or the ones you have on.
            List<string?> pools = Stones?.Select(name => Shiftstones.Pool(name) ?? Unknown(name)).ToList()
                                  ?? Shiftstones.EquippedBy(you).Select(p => (string?)p).ToList();
            for (var slot = 0; slot < pools.Count; slot++)
            {
                var pool = pools[slot];
                if (pool == null) continue;

                ShiftStone? copy = PoolManager.instance.GetPooledObject(pool)?.GetComponent<ShiftStone>();
                if (copy == null)
                {
                    report.Add($"{slot}: {pool} NOT in a pool");
                    continue;
                }

                actor.PlayerShiftstones.AttachShiftStone(copy, slot, false, false);
                report.Add($"{slot}: {pool}");
            }
        }
        catch (Exception e)
        {
            report.Add($"failed: {e.Message}");
        }
        finally
        {
            actor.controllerType = simulatedAs;
        }

        Mod.Note("Actor.Shiftstones", $"{this}: {(report.Count == 0 ? "none equipped" : string.Join(", ", report))}", echo: false);
    }

    private string? Unknown(string name)
    {
        Mod.Note("Actor.UnknownStone", $"{this}: no shiftstone called \"{name}\" - there are {string.Join(", ", Shiftstones.Catalog().Select(s => s.Pool))}");
        return null;
    }

    private static void DisableAll<T>(GameObject body) where T : Behaviour
    {
        foreach (T behaviour in body.GetComponentsInChildren<T>(true)) behaviour.enabled = false;
    }

    private void Forget()
    {
        Mod.Actors.Remove(this);
        _player = null;
        _rig = null;
        _pose = null;
        Ready = false;
        _remoteUi = null;
        _nameTag = null;
        _localUi = null;
        _ownColliders = null;
        _planted = false;
        _knockedOut = false;
        _clip = null;
        _take = null;
        Input.Clear();
        ForgetPuppet(); // PUPPET
    }

    // ---- Control ---------------------------------------------------------------------------------

    /// <summary>Holds this pose until another is set - the head-and-hands source for bot control.</summary>
    public void SetPose(ActorPose pose) => _pose = pose;

    public bool PlayingClip => Exists && _clip != null;

    /// <summary>
    /// Plays a recorded performance: its poses become the actor's headset and controllers and its
    /// sticks the actor's sticks, so the game recognises the gestures and runs the moves itself.
    /// Bot control only. A planted actor stays put however the clip walks.
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

    // ---- Takes -----------------------------------------------------------------------------------

    /// <summary>A take being performed: the clip, which part, against which clock, in which lane.</summary>
    private sealed record Take(Clip Clip, double From, double StartedAt, double? Until, Services.Charts.LaneFrame Lane, Func<double> Clock)
    {
        public int NextMove { get; set; }

        /// <summary>Moves as recorded, runs none of the moves.</summary>
        public bool Mime { get; init; }

        public float PathError { get; set; }
    }

    public bool PlayingTake => Exists && _take != null;

    /// <summary>The worst distance the actor was found from the take's path before being put back on it - the self-test reads it.</summary>
    public float WorstTakePathError { get; private set; }

    /// <summary>How far the actor was from the take's path this frame, before being put back on it.</summary>
    public float LastTakePathError { get; private set; }

    /// <summary>How many of a take's moves this actor has performed.</summary>
    public int TakeMovesPlayed { get; private set; }

    /// <summary>
    /// Performs a recorded take exactly: its poses drive the actor's headset and controllers, its
    /// moves run at the times they were made (the actor's own gesture recognition is off meanwhile,
    /// so nothing fires twice or late), and the actor is kept on the take's path in
    /// <paramref name="lane"/>. Plays the clip from <paramref name="from"/>, starting now, until chart
    /// time <paramref name="until"/> or the clip's end; <paramref name="clock"/> is the chart's clock.
    /// </summary>
    public void PlayTake(Clip clip, double from, double? until, Services.Charts.LaneFrame lane, Func<double> clock, bool mime = false)
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

        // The path is held below, so the walk stick would only fight it; turning still comes from the take.
        Input.Walk = Vector2.zero;
        // A take with facings is turned to them below; the stick replay would turn it twice.
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
    /// Puts the actor back on the take's path: the floor under its head is compared with where the
    /// performer stood, and both physics bodies are shifted by the difference. Moving the bodies
    /// directly works whatever the plant constraints say, and keeps any vertical motion - a jump.
    /// </summary>
    private void FollowPath(Services.Charts.LaneFrame lane, System.Numerics.Vector2 spot)
    {
        Vector3 wanted = Describe.ToUnity(lane.ToWorld(new System.Numerics.Vector3(spot.X, 0f, spot.Y)));
        Vector3 actual = _rig!.FloorPoint;
        var shift = new Vector3(wanted.x - actual.x, 0f, wanted.z - actual.z);

        // The first frames snap the actor from where it was placed onto the take's start; only the
        // error once it is following counts.
        var error = shift.magnitude;
        LastTakePathError = error;
        if (_take != null && _take.Clock() - _take.StartedAt > 0.5) WorstTakePathError = Math.Max(WorstTakePathError, error);
        if (error < 0.01f) return;

        // A big gap - a section joining another take, where the two performers stood apart - is
        // closed over a few frames rather than in one jump.
        if (error > 0.25f) shift *= Mathf.Clamp01(Time.deltaTime * 10f);

        foreach (Rigidbody? body in PlantedBodies())
        {
            body?.position += shift;
        }
    }

    /// <summary>
    /// Turns the actor to the facing the performer had, with the game's own rotation - replaying
    /// the turn stick alone drifted (snap turns trigger on thresholds, smooth turns on frame time).
    /// </summary>
    private void FollowFacing(Services.Charts.LaneFrame lane, float laneYaw)
    {
        var wanted = lane.ToWorldYaw(laneYaw);
        var angle = Mathf.DeltaAngle(_rig!.Vr.eulerAngles.y, wanted);
        if (Mathf.Abs(angle) < 1f) return;

        _player!.Controller.PlayerMovement.Rotate(angle, false);
        WorstTakeFacingError = Math.Max(WorstTakeFacingError, _take != null && _take.Clock() - _take.StartedAt > 0.5 ? Mathf.Abs(angle) : 0f);
    }

    /// <summary>The worst turn needed to keep the actor to a take's facing, once following - the self-test reads it.</summary>
    public float WorstTakeFacingError { get; private set; }

    public void SetControl(ActorControl control)
    {
        Control = control;
        Input.Clear();
        Mod.Note("Actor.Control", $"{this}: {control}");
    }

    /// <summary>Runs one of the actor's moves by name (a stack's CachedName, e.g. "Straight"), no gesture involved.</summary>
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

    /// <summary>
    /// Moves the actor to <paramref name="floorPosition"/> with the game's own
    /// <c>PlayerMovement.Reposition</c> (what puts players on spawn points) and plants it there.
    /// </summary>
    public void Place(Vector3 floorPosition, float yawDegrees)
    {
        if (!IsSimulated) return;

        Unplant();

        // Reposition places the head, not the feet: sent to a floor point, the second self-test
        // measured the actor's feet 1.3 m under the floor - its eye height.
        var eyeHeight = _rig == null ? 0f : _rig.Head.position.y - _rig.Vr.position.y;
        _player!.Controller.PlayerMovement.Reposition(floorPosition + (Vector3.up * eyeHeight), Quaternion.Euler(0f, yawDegrees, 0f));
        Plant();
        Mod.Note("Actor.Placed", $"{this} at {Describe.Vector(floorPosition)} facing {yawDegrees:F0}", echo: false);
        MelonCoroutines.Start(SetDown(_player, floorPosition.y));
        MelonCoroutines.Start(CheckPlacement(_player, floorPosition));
    }

    /// <summary>
    /// Sets a placed actor down onto the floor: Reposition places the head, and an actor whose pose
    /// was not yet applied came out up to a metre high and dropped into place. A couple of frames
    /// later its feet are measured against the floor it was sent to - never a ray, which in the Ring
    /// found a surface under the floor and pulled an actor through it - and it is moved down onto it.
    /// </summary>
    private IEnumerator SetDown(Player player, float floor)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            // Real time, not frames: at a high frame rate two frames pass before physics has moved the
            // bodies, and a second pass then read the old height and set the actor down twice.
            yield return new WaitForSeconds(0.08f);
            if (!Owns(player) || !Exists || _rig == null) yield break;

            var above = _rig.FloorPoint.y - floor;
            if (above is < 0.05f or > 2.5f) yield break;

            foreach (Rigidbody? body in PlantedBodies())
            {
                if (body == null) continue;
                body.position += Vector3.down * above;
                body.velocity = Vector3.zero;
            }

            Mod.Note("Actor.SetDown", $"{this}: {above:0.00} m above the floor - set down", echo: false);
        }
    }

    /// <summary>
    /// Reposition is the game's own call, but what it places - feet, rig, root - is taken on trust;
    /// half a second later the actor's floor point is measured against where it was sent.
    /// </summary>
    private IEnumerator CheckPlacement(Player player, Vector3 wanted)
    {
        yield return new WaitForSeconds(PlacementCheckSeconds);
        if (!Owns(player) || !Exists || _rig == null) yield break;

        // Across and along the lane is what counts: on uneven ground (the Gym) the actor settles on
        // the real floor, which can be well above or below the lane's own height.
        Vector3 actual = _rig.FloorPoint;
        var off = Vector2.Distance(new Vector2(actual.x, actual.z), new Vector2(wanted.x, wanted.z));
        var close = off <= PlacementTolerance && actual.y > wanted.y - 2f;
        Mod.Note(close ? "Actor.PlacementOk" : "Actor.PlacementOff",
            $"{this}: sent to {Describe.Vector(wanted)}, stands at {Describe.Vector(actual)} ({off:F2} m off, {actual.y - wanted.y:+0.00;-0.00} m in height)", echo: !close);
    }

    /// <summary>
    /// Freezes X and Z on its physics body and VR rig rigidbodies. Physics still runs - gravity,
    /// jumps, landing - but nothing slides it. The game's own Rotate sets the constraints to
    /// FreezeAll and restores them afterwards, so the bits are OR'd back each frame
    /// (<see cref="KeepPlanted"/>) and only those bits are ever cleared.
    /// </summary>
    public void Plant()
    {
        if (!IsSimulated) return;
        _planted = true;
        KeepPlanted();
    }

    public void Unplant()
    {
        if (!Exists) return;

        _planted = false;
        foreach (Rigidbody? body in PlantedBodies())
        {
            body?.constraints &= ~PlantConstraints;
        }
    }

    private void KeepPlanted()
    {
        foreach (Rigidbody? body in PlantedBodies())
        {
            if (body == null || (body.constraints & PlantConstraints) == PlantConstraints) continue;
            body.constraints |= PlantConstraints;
        }
    }

    /// <summary>The actor's colliders - for what it should pass through.</summary>
    public IEnumerable<Collider> Colliders => Exists ? _player!.Controller.GetComponentsInChildren<Collider>(true) : Array.Empty<Collider>();

    private Rigidbody?[] PlantedBodies()
    {
        PlayerPhysics? physics = _player?.Controller?.PlayerPhysics;
        return physics == null ? Array.Empty<Rigidbody?>() : new[] { physics.PhysicsRigidbody, physics.VRrigidbody };
    }

    /// <summary>
    /// Stops a structure colliding with the actor's body for a while - one launched from beside an
    /// actor would otherwise be deflected by it. Undone afterwards: structures are pooled, and the
    /// same disc may later be yours, aimed at the actor.
    /// </summary>
    public void IgnoreStructure(GameObject structure, float seconds)
    {
        if (!Exists) return;

        _ownColliders ??= _player!.Controller.GetComponentsInChildren<Collider>(true);
        Collider[] structureColliders = structure.GetComponentsInChildren<Collider>(true);

        foreach (Collider mine in _ownColliders)
        {
            foreach (Collider theirs in structureColliders) Physics.IgnoreCollision(mine, theirs, true);
        }

        MelonCoroutines.Start(RestoreCollisionWhenClear(_ownColliders, structureColliders, structure.transform, seconds));
    }

    /// <summary>How far a structure must be from the actor before it can hit the actor again.</summary>
    private const float ClearOfActor = 1.6f;

    /// <summary>
    /// Collisions back once the structure is clear of the actor - not after a fixed time: a chart
    /// projectile punched straight back within its 2.5 s went through the boss (the self-test's
    /// hitbox check) - or after <paramref name="seconds"/> at the latest.
    /// </summary>
    private IEnumerator RestoreCollisionWhenClear(Collider[] mine, Collider[] theirs, Transform structure, float seconds)
    {
        var until = Time.time + seconds;
        while (Time.time < until && structure != null && Exists)
        {
            Vector3 body = _player!.Controller.transform.position;
            var apart = new Vector2(structure.position.x - body.x, structure.position.z - body.z);
            if (apart.magnitude > ClearOfActor) break;
            yield return null;
        }

        foreach (Collider a in mine)
        {
            if (a == null) continue;
            foreach (Collider b in theirs)
            {
                if (b != null) Physics.IgnoreCollision(a, b, false);
            }
        }
    }

    // ---- Knock-outs ------------------------------------------------------------------------------

    /// <summary>
    /// The actor's stand-in for death: HP shows 0 and the pose holds; after
    /// <see cref="KnockOutSeconds"/> it is back on full health. The game's own death handling never
    /// runs for it - for a stripped clone that path crashed the process.
    /// </summary>
    public void KnockOut(string reason)
    {
        if (!Exists || _knockedOut) return;

        _knockedOut = true;
        KnockOuts++;

        PlayerHealth health = _player!.Controller.PlayerHealth;
        var previous = _player.Data.HealthPoints;
        _player.Data.HealthPoints = 0;
        health.SetHealthBarPercentage(0f, previous, true);

        Mod.Note("Actor.KnockedOut", $"{this} #{KnockOuts}: {reason}");
        WasKnockedOut?.Invoke(this);
        MelonCoroutines.Start(ReviveAfterKnockOut(_player));
    }

    /// <summary>
    /// Full health now. A new player starts low and the game fills it up over a second or two -
    /// over by the end of the usual countdown, but a 0 s countdown would start the fight on it.
    /// </summary>
    public void FullHealth()
    {
        if (!Exists || _knockedOut) return;

        var hp = _player!.Data.HealthPoints;
        if (hp >= MaxHealth) return;

        _player.Controller.PlayerHealth.SetHealth(MaxHealth, hp, false);
        Mod.Note("Actor.FullHealth", $"{this}: {hp} -> {MaxHealth} at GO", echo: false);
    }

    private IEnumerator ReviveAfterKnockOut(Player player)
    {
        yield return new WaitForSeconds(KnockOutSeconds);
        if (!Owns(player) || !Exists) yield break;

        try
        {
            player.Controller.PlayerHealth.SetHealth(MaxHealth, player.Data.HealthPoints, true);
        }
        catch (Exception e)
        {
            Mod.Note("Actor.ReviveFailed", $"{this}: {e}");
        }
        finally
        {
            _knockedOut = false;
        }
    }

    // ---- Per frame -------------------------------------------------------------------------------

    public void Update()
    {
        if (!Exists || _rig == null) return;

        if (Mode == ActorMode.Puppet)
        {
            UpdatePuppet(); // PUPPET
            return;
        }

        // Calls are briefly run as Remote (ActorPatches); if one ever ended without flipping back,
        // the actor would silently stop being simulated.
        PlayerController controller = _player!.Controller;
        if (controller.controllerType != ControllerType.Local)
        {
            controller.controllerType = ControllerType.Local;
            Mod.Note("Actor.ControllerTypeRestored", $"{this}: found Remote outside a guarded call - set back to Local");
        }

        if (_planted) KeepPlanted();
        if (_knockedOut) return;

        if (Control == ActorControl.Mirror)
        {
            PlayerController? you = PlayerManager.instance?.localPlayer?.Controller;
            if (you == null) return;

            var yours = ActorRig.Of(you);
            ActorRig.CopyLocal(yours.Offset, _rig.Offset);
            ActorRig.CopyLocal(yours.Head, _rig.Head);
            ActorRig.CopyLocal(yours.LeftHand, _rig.LeftHand);
            ActorRig.CopyLocal(yours.RightHand, _rig.RightHand);
            return;
        }

        if (_take != null) PlayTakeFrame();
        else if (_clip != null) PlayClipFrame();
        else _pose?.ApplyTo(_rig);

        if (Input.FaceYou && Time.time >= _nextFacingCheck)
        {
            _nextFacingCheck = Time.time + FacingCheckInterval;
            TurnTowards(PlayerManager.instance?.localPlayer?.Controller);
        }
    }

    /// <summary>Turns toward a player with the game's own rotation, as a turn input would.</summary>
    private void TurnTowards(PlayerController? target)
    {
        if (target == null || _rig == null) return;

        Vector3 toTarget = ActorRig.Of(target).Head.position - _rig.Head.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.01f) return;

        var angle = Vector3.SignedAngle(_rig.Facing, toTarget.normalized, Vector3.up);
        if (Mathf.Abs(angle) < FacingTolerance) return;

        _player!.Controller.PlayerMovement.Rotate(angle, false);
    }

    /// <summary>
    /// The player UI the game hangs over a player - nameplate, the floating health bar with its mute
    /// icon, the first-person bar - is all hidden on an actor: a battle scores damage over the run, so
    /// the boss's health only cluttered the view. Hidden by scale rather than deactivated -
    /// NameBending's coroutine throws on an inactive plate.
    /// </summary>
    public void LateUpdate()
    {
        if (Mode != ActorMode.Simulated || !Exists || _rig == null) return;

        HideByScale(_nameTag);
        HideByScale(_localUi);
        HideByScale(_remoteUi);
    }

    private static void HideByScale(Transform? part)
    {
        if (part != null && part.localScale != Vector3.zero) part.localScale = Vector3.zero;
    }
}
