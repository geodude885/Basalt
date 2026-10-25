using Basalt.Battle;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPhoton.Pun;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Scaling;
using Il2CppRUMBLE.Players.Subsystems;
using UnityEngine;
using UnityEngine.InputSystem.XR;

namespace Basalt.Actors;

// Making an actor's body: a second Local player, drawn and heard as a remote one.
internal static class ActorSpawner
{
    private static readonly string[] PlayerUiPaths = { "NameTag", "UI/LocalUI", "UI/RemoteUI" };

    public static Player Create(Actor actor, Vector3 floorPosition, Quaternion facing)
    {
        var local = BattlePlayer.Player!;

        var shiftstones = new Il2CppStructArray<short>(2);
        shiftstones[0] = -1;
        shiftstones[1] = -1;

        var data = new PlayerData(
            new GeneralData
            {
                PlayFabMasterId = $"Basalt-{actor.Id}-{Guid.NewGuid():N}",
                PlayFabTitleId = Guid.NewGuid().ToString(),
                BattlePoints = local.Data.GeneralData?.BattlePoints ?? 0,
                PublicUsername = actor.Name,
            },
            local.Data.RedeemedMoves,
            local.Data.EconomyData,
            shiftstones,
            PlayerVisualData.FromPlayfabDataString(local.Data.VisualData.ToPlayfabDataString()))
        {
            PlayerMeasurement = actor.Measurement is { } size ? new PlayerMeasurement { Length = size.Height, ArmSpan = size.ArmSpan } : local.Data.PlayerMeasurement,
        };

        var player = Player.CreateRemotePlayer(data);

        // PlayerController.Initialize types a player Local when its actor number is PhotonNetwork.LocalPlayer's.
        player.AssignActorNo((short)(PhotonNetwork.LocalPlayer?.ActorNumber ?? 0));

        PlayerManager.instance.SpawnPlayerController(player, floorPosition, facing);
        return player;
    }

    public static ActorBody Setup(Actor actor, Player player, Vector3 floorPosition)
    {
        var controller = player.Controller;
        var body = controller.gameObject;
        body.name = $"Basalt_Actor_{actor.Id}";

        body.DisableAll<PlayerResetSystem>();
        body.DisableAll<PlayerHaptics>();
        body.DisableAll<PlayerLIV>();
        body.DisableAll<PlayerCollisionFade>();
        foreach (var camera in body.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
        foreach (var listener in body.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
        foreach (var driver in body.GetComponentsInChildren<TrackedPoseDriver>(true)) driver.enabled = false;
        foreach (var source in body.GetComponentsInChildren<AudioSource>(true)) source.spatialBlend = 1f;

        // Only add to player list until visible to avoid stucture physics issues
        SetInPlayerList(player, true);
        player.Data.VisualData = ActorLook.Resolve(actor, BattlePlayer.Player!.Data.VisualData);
        PresentAsRemote(actor, controller);

        Shiftstones.CopyOnto(actor, controller, BattlePlayer.Player?.Controller!);
        var hiddenUi = PlayerUiPaths.Select(path => body.transform.Find(path)).Where(part => part != null).ToArray();
        foreach (var part in hiddenUi) part.localScale = Vector3.zero;

        var rig = PlayerRig.From(controller);
        var footing = new ActorFooting(actor, controller, rig);
        var clips = new ClipPlayer(actor, controller, rig, footing, ActorPose.DefaultPose(PlayerRig.From(BattlePlayer.Player?.Controller!)));

        Logger.Info($"[Actor.Spawned] {actor}: type {controller.ControllerType}, look \"{actor.PlayerCustomisation}\", at {floorPosition.Describe()}; " +
            $"moves: {string.Join(", ", controller.PlayerProcessor.availableStacks.ToArray().Select(s => s.CachedName))}");

        return new ActorBody(player, controller, rig, footing, clips, new ActorCollisions(controller), hiddenUi);
    }

    // Once its look is baked (or given up on): shown, and out of the player list again.
    public static void Reveal(Actor actor, ActorBody body, float waitedSeconds)
    {
        var visuals = body.Controller.PlayerVisuals;
        if (visuals.Renderer != null && !visuals.Renderer.enabled) visuals.Renderer.enabled = true;

        var baked = visuals.VisualsAreReadyAndApplied;
        Logger.Write(baked ? LogLevel.Debug : LogLevel.Warning, $"[Actor.Visuals] {actor}: {(baked ? "baked" : "NOT baked")} after {waitedSeconds:F2}s");
        SetInPlayerList(body.Player, false);
    }

    private static void SetInPlayerList(Player player, bool inList)
    {
        var players = PlayerManager.instance.AllPlayers;
        players.Remove(player);
        if (inList) players.Add(player);
    }

    private static void PresentAsRemote(Actor actor, PlayerController controller)
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
            Logger.Error($"[Actor.PresentationFailed] {actor}: {e}");
        }
        finally
        {
            controller.controllerType = simulatedAs;
        }
    }
}
