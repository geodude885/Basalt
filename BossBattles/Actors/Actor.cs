using System.Collections;
using BossBattles.Core;
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

namespace BossBattles.Actors;

/// <summary>
/// A game-simulated player that is not you: what a battle puts in the lane. Created as a Local
/// player, so the game runs its movement, physics, moves and shiftstones; drawn as a Remote one, so
/// it has a body and its sounds play in the world. See CLAUDE.md, "Extra players".
/// </summary>
internal sealed partial class Actor
{
    private const float SpawnTimeoutSeconds = 5f;
    private const float VisualsTimeoutSeconds = 10f;
    private const float FacingCheckInterval = 0.25f;
    private const float FacingTolerance = 15f;

    private Player? _player;
    private ActorRig? _rig;
    private ActorPose? _pose;
    private bool _spawning;
    private Transform? _remoteUi;
    private Transform? _nameTag;
    private Transform? _localUi;
    private float _nextFacingCheck;

    public Actor(string id, string name, string look, IReadOnlyList<string>? stones = null)
    {
        Id = id;
        Name = name;
        Look = look;
        Stones = stones;
    }

    /// <summary>The stones it wears, by name; null for the ones you have equipped.</summary>
    public IReadOnlyList<string>? Stones { get; }

    public string Id { get; }

    public string Name { get; }

    /// <summary>"you", "default", "defaultFemale", "defaultMale", or a look string as the game stores it.</summary>
    public string Look { get; }

    public bool Exists => _player != null && _player.Controller != null;

    /// <summary>Spawned, with its body baked and on show.</summary>
    public bool Ready { get => Exists && field; private set; }

    public Player? Player => Exists ? _player : null;

    public Vector3? HeadPosition => Exists && _rig != null ? _rig.Head.position : null;

    public ActorControl Control { get; private set; } = ActorControl.Bot;

    public ActorInput Input { get; } = new();

    /// <summary>The actor's sticks come from <see cref="Input"/>, not your controllers.</summary>
    public bool IsBotControlled => Exists && Control == ActorControl.Bot;

    public event Action<Actor>? BecameReady;

    public bool Owns(Player player) => _player != null && _player.Pointer == player.Pointer;

    public override string ToString() => $"{Name} [{Id}]";

    /// <summary>Creates the body; asynchronous, see <see cref="BecameReady"/>.</summary>
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

    public void SetControl(ActorControl control)
    {
        Control = control;
        Input.Clear();
        Mod.Note("Actor.Control", $"{this}: {control}");
    }

    public void Update()
    {
        if (!Exists || _rig == null) return;

        // Calls are briefly run as Remote (ActorPatches); one that never flipped back would stop the simulation.
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

    /// <summary>
    /// Hides the nameplate and health bars the game hangs over a player: a battle scores damage over
    /// the run, so they only clutter. By scale, not deactivation - NameBending throws on an inactive plate.
    /// </summary>
    public void LateUpdate()
    {
        if (!Exists || _rig == null) return;

        HideByScale(_nameTag);
        HideByScale(_localUi);
        HideByScale(_remoteUi);
    }

    private static void HideByScale(Transform? part)
    {
        if (part != null && part.localScale != Vector3.zero) part.localScale = Vector3.zero;
    }

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
            else Setup(player, floorPosition);
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

        // Your look, whatever the actor's: a Local player's PlayerVisuals.Initialize caches its look as
        // the local look, so any other would turn you into it. The actor's is applied as Remote later.
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

        // PlayerController.Initialize types a player Local when its actor number is PhotonNetwork.LocalPlayer's.
        player.AssignActorNo((short)(PhotonNetwork.LocalPlayer?.ActorNumber ?? 0));

        PlayerManager.instance.SpawnPlayerController(player, floorPosition, facing);
        return player;
    }

    private void Setup(Player player, Vector3 floorPosition)
    {
        PlayerController controller = player.Controller;
        GameObject body = controller.gameObject;
        body.name = $"BossBattles_Actor_{Id}";

        // Resets or kills falling players, and a Local one listens for XR recentering meant for you.
        DisableAll<PlayerResetSystem>(body);
        DisableYourExtras(body);

        _player = player;
        Mod.Actors.Add(this);

        // The body is baked only while listed in AllPlayers, but a second listed player stops
        // structure-on-structure collisions registering in the Gym: listed until FinishWhenVisible.
        SetInPlayerList(true);
        player.Data.VisualData = ResolveLook();

        PresentAsRemote(controller);
        CopyShiftstones(controller, PlayerManager.instance.localPlayer.Controller);
        _remoteUi = body.transform.Find("UI/RemoteUI");
        _nameTag = body.transform.Find("NameTag");
        _localUi = body.transform.Find("UI/LocalUI");

        _rig = ActorRig.Of(controller);
        _pose = ActorPose.Guard(ActorRig.Of(PlayerManager.instance.localPlayer.Controller));

        Mod.Note("Actor.Spawned",
            $"{this}: type {controller.ControllerType}, look \"{Look}\", at {Describe.Vector(floorPosition)}; " +
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
    /// Turns off a Local player's extras that would act on you - camera, audio listener, haptics, LIV,
    /// screen fade, pose drivers reading your headset - and makes its 2D sounds positional.
    /// </summary>
    private static void DisableYourExtras(GameObject body)
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
    /// PlayerVisuals, PlayerNameTag and PlayerUI each decide from <c>controllerType</c> in their
    /// Initialize whether this is you: re-run them as Remote, then flip back.
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
    /// Attaches the chart's stones, or yours, fresh from their pools. The flags keep it out of your
    /// save and off the network; run as Remote so the stones are not put on a first-person layer.
    /// </summary>
    private void CopyShiftstones(PlayerController actor, PlayerController you)
    {
        var report = new List<string>();
        ControllerType simulatedAs = actor.controllerType;
        actor.controllerType = ControllerType.Remote;

        try
        {
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

    private static void DisableAll<T>(GameObject body)
        where T : Behaviour
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
    }
}
