using System.Collections;
using BossBattles.Battle;
using BossBattles.Services.Charts;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPhoton.Pun;
using Il2CppRUMBLE.Combat.ShiftStones;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Scaling;
using Il2CppRUMBLE.Players.Subsystems;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem.XR;

namespace BossBattles.Actors;

internal partial class Actor(string id, string name, string customisationString, IReadOnlyList<string>? stones = null)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string PlayerCustomisation { get; } = customisationString;
    public IReadOnlyList<string>? Stones { get; } = stones;
    public BodySize? Measurement { get; init; }
    public Player? Player { get; private set; }
    // Can't use normal null coalesce on destroyed unity objs
    public PlayerController? Controller => Player?.Controller is { } controller && controller != null ? controller : null;

    public bool Ready { get => Controller != null && field; private set; }
    public Vector3? HeadPosition => Controller != null && _rig != null ? _rig.Head.position : null;

    public event Action<Actor>? OnReady;

    public bool IsBodyOf(Player player) => Player != null && Player.Pointer == player.Pointer;

    private const float SpawnTimeoutSeconds = 5f;
    private const float VisualsTimeoutSeconds = 10f;

    private static readonly string[] PlayerUiPaths = { "NameTag", "UI/LocalUI", "UI/RemoteUI" };

    private ActorRig? _rig;
    private ActorPose? _pose;
    private bool _spawning;
    private Transform[] _playerUi = [];


    /// <summary>Creates the body asynchronously, see <see cref="OnReady"/>.</summary>
    public void Spawn(Vector3 floorPosition, Quaternion facing)
    {
        if (_spawning || Controller != null)
        {
            Logger.Info($"[Actor.SpawnSkipped] {this}: {(_spawning ? "already spawning" : "already exists")}");
            return;
        }

        if (BattlePlayer.Controller == null)
        {
            Logger.Info($"[Actor.SpawnSkipped] {this}: no local player to model it on");
            return;
        }

        _spawning = true;
        MelonCoroutines.Start(SpawnRoutine(floorPosition, facing));
    }

    public void Despawn()
    {
        if (Player != null)
        {
            PlayerManager.instance?.AllPlayers?.Remove(Player);
            if (Controller is { } controller) UnityEngine.Object.Destroy(controller.gameObject);
            Logger.Info($"[Actor.Despawned] {this}: {Player.Describe()}");
        }

        Forget();
    }

    public void ForgetSceneObjects()
    {
        if (Player != null) PlayerManager.instance?.AllPlayers?.Remove(Player);
        Forget();
    }

    public void Update()
    {
        if (Controller is not { } controller || _rig == null) return;

        if (controller.controllerType != ControllerType.Local)
        {
            controller.controllerType = ControllerType.Local;
            Logger.Warning($"[Actor.ControllerTypeRestored] {this}: found Remote outside a guarded call - set back to Local");
        }

        if (_planted) KeepPlanted();

        if (_clip != null) PlayClipFrame();
        else _pose?.ApplyTo(_rig);
    }

    public void LateUpdate()
    {
        if (Controller == null || _rig == null) return;

        foreach (var part in _playerUi)
        {
            // Hides the nameplate and health bars by scaleing to 0. NameBending can throw on an inactive plate.
            if (part != null && part.localScale != Vector3.zero) part.localScale = Vector3.zero;
        }
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
            Logger.Error($"[Actor.SpawnFailed] {this}: {e}");
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
            if (player.Controller == null) Logger.Error($"[Actor.SpawnFailed] {this}: no controller after {SpawnTimeoutSeconds}s");
            else Setup(player, floorPosition);
        }
        catch (Exception e)
        {
            Logger.Error($"[Actor.SpawnFailed] {this}: {e}");
        }

        _spawning = false;
        if (Controller != null) MelonCoroutines.Start(FinishWhenVisible(player));
    }

    private Player Create(Vector3 floorPosition, Quaternion facing)
    {
        var local = BattlePlayer.Player!;

        var shiftstones = new Il2CppStructArray<short>(2);
        shiftstones[0] = -1;
        shiftstones[1] = -1;

        var data = new PlayerData(
            new GeneralData
            {
                PlayFabMasterId = $"BossBattles-{Id}-{Guid.NewGuid():N}",
                PlayFabTitleId = Guid.NewGuid().ToString(),
                BattlePoints = local.Data.GeneralData?.BattlePoints ?? 0,
                PublicUsername = Name,
            },
            local.Data.RedeemedMoves,
            local.Data.EconomyData,
            shiftstones,
            PlayerVisualData.FromPlayfabDataString(local.Data.VisualData.ToPlayfabDataString()))
        {
            PlayerMeasurement = Measurement is { } size ? new PlayerMeasurement { Length = size.Height, ArmSpan = size.ArmSpan } : local.Data.PlayerMeasurement,
        };

        var player = Player.CreateRemotePlayer(data);

        // PlayerController.Initialize types a player Local when its actor number is PhotonNetwork.LocalPlayer's.
        player.AssignActorNo((short)(PhotonNetwork.LocalPlayer?.ActorNumber ?? 0));

        PlayerManager.instance.SpawnPlayerController(player, floorPosition, facing);
        return player;
    }

    private void Setup(Player player, Vector3 floorPosition)
    {
        var controller = player.Controller;
        var body = controller.gameObject;
        body.name = $"BossBattles_Actor_{Id}";

        body.DisableAll<PlayerResetSystem>();
        body.DisableAll<PlayerHaptics>();
        body.DisableAll<PlayerLIV>();
        body.DisableAll<PlayerCollisionFade>();
        foreach (var camera in body.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
        foreach (var listener in body.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
        foreach (var driver in body.GetComponentsInChildren<TrackedPoseDriver>(true)) driver.enabled = false;
        foreach (var source in body.GetComponentsInChildren<AudioSource>(true)) source.spatialBlend = 1f;

        Player = player;

        // Only add to player list until visible to avoid stucture physics issues
        SetInPlayerList(true);
        player.Data.VisualData = ResolveLook();
        PresentAsRemote(controller);

        CopyShiftstones(controller, BattlePlayer.Controller!);
        _playerUi = PlayerUiPaths.Select(path => body.transform.Find(path)).Where(part => part != null).ToArray();

        _rig = ActorRig.From(controller);
        _pose = ActorPose.DefaultPose(ActorRig.From(BattlePlayer.Controller!));

        Logger.Info($"[Actor.Spawned] {this}: type {controller.ControllerType}, look \"{PlayerCustomisation}\", at {floorPosition.Describe()}; " +
            $"moves: {string.Join(", ", controller.PlayerProcessor.availableStacks.ToArray().Select(s => s.CachedName))}");
    }

    private IEnumerator FinishWhenVisible(Player player)
    {
        var start = Time.realtimeSinceStartup;

        while (IsBodyOf(player) && Controller != null && !player.Controller.PlayerVisuals.VisualsAreReadyAndApplied
               && Time.realtimeSinceStartup - start < VisualsTimeoutSeconds)
        {
            yield return null;
        }

        if (!IsBodyOf(player) || Controller == null) yield break;

        try
        {
            var bodyRenderer = player.Controller.PlayerVisuals.Renderer;
            if (bodyRenderer != null && !bodyRenderer.enabled) bodyRenderer.enabled = true;

            var baked = player.Controller.PlayerVisuals.VisualsAreReadyAndApplied;
            Logger.Write(baked ? LogLevel.Debug : LogLevel.Warning, $"[Actor.Visuals] {this}: {(baked ? "baked" : "NOT baked")} after {Time.realtimeSinceStartup - start:F2}s");
            SetInPlayerList(false);

            Ready = true;
            OnReady?.Invoke(this);
        }
        catch (Exception e)
        {
            Logger.Error($"[Actor.VisualsFailed] {this}: {e}");
        }
    }

    private void SetInPlayerList(bool inList)
    {
        if (Player is null) return;
        var players = PlayerManager.instance.AllPlayers;
        players.Remove(Player);
        if (inList) players.Add(Player);
    }

    private PlayerVisualData ResolveLook()
    {
        var playerLook = PlayerVisualData.FromPlayfabDataString(BattlePlayer.Player!.Data.VisualData.ToPlayfabDataString());

        switch (PlayerCustomisation.Trim().ToLowerInvariant())
        {
            case "":
            case "you":
                return playerLook;
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
            return PlayerVisualData.FromPlayfabDataString(PlayerCustomisation);
        }
        catch (Exception e)
        {
            Logger.Warning($"[Actor.LookInvalid] {this}: \"{PlayerCustomisation}\" is not a look the game can read ({e.Message}) - using yours");
            return playerLook;
        }
    }

    private void PresentAsRemote(PlayerController controller)
    {
        var simulatedAs = controller.controllerType;
        controller.controllerType = ControllerType.Remote;

        try
        {
            controller.PlayerVisuals.Initialize(controller);
            controller.PlayerNameTag.Initialize(controller);
            controller.PlayerUI.Initialize(controller);
        }
        catch (Exception e)
        {
            Logger.Error($"[Actor.PresentationFailed] {this}: {e}");
        }
        finally
        {
            controller.controllerType = simulatedAs;
        }
    }

    private void CopyShiftstones(PlayerController to, PlayerController from)
    {
        var report = new List<string>();
        var simulatedAs = to.controllerType;
        to.controllerType = ControllerType.Remote;

        try
        {
            var pools = Stones?.Select(Shiftstones.ToIdOrDefault).ToList() ?? Shiftstones.EquippedBy(from).Select(p => (string?)p).ToList();
            foreach ((var pool, var slot) in pools.Select((p, slot) => (p, slot)))
            {
                if (pool == null)
                {
                    Logger.Warning($"[Actor.UnknownStone] {this}: no shiftstone called \"{Stones![slot]}\"");
                    continue;
                }

                var copy = PoolManager.instance.GetPooledObject(pool)?.GetComponent<ShiftStone>();
                if (copy == null)
                {
                    report.Add($"{slot}: {pool} NOT in a pool");
                    continue;
                }

                to.PlayerShiftstones.AttachShiftStone(copy, slot, false, false);
                report.Add($"{slot}: {pool}");
            }
        }
        catch (Exception e)
        {
            report.Add($"failed: {e.Message}");
        }
        finally
        {
            to.controllerType = simulatedAs;
        }

        Logger.Debug($"[Actor.Shiftstones] {this}: {(report.Count == 0 ? "none equipped" : string.Join(", ", report))}");
    }


    private void Forget()
    {
        Player = null;
        Ready = false;
        _rig = null;
        _pose = null;
        _playerUi = [];
        _colliders = null;
        _planted = false;
        _clip = null;
        _clipFingers = null;
    }
}
