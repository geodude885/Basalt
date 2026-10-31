using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Basalt.Battle;
using Basalt.Services.Charts.Model;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;
using MelonLoader;
using UnityEngine;

namespace Basalt.Actors;

internal class Actor(string id, string name, string customisationString, IReadOnlyList<string>? stones = null)
{
    public const short MaxHealth = 20;

    private const float SpawnTimeoutSeconds = 5f;
    private const float VisualsTimeoutSeconds = 10f;

    private bool _spawning;

    public string Id { get; } = id;
    public string Name { get; } = name;
    public string PlayerCustomisation { get; } = customisationString;
    public IReadOnlyList<string>? Stones { get; } = stones;
    public BodySize? Measurement { get; init; }
    public float SelfKnockbackMultiplier { get; set; } = 1f;

    public ActorBody? Body { get; private set; }

    // Spawned, its look baked, and its body not destroyed, only then Body is safe to use.
    [MemberNotNullWhen(true, nameof(Body))]
    public bool Ready { get => field && Body != null && Body.Controller != null; private set; }

    public event Action<Actor>? OnReady;

    public bool IsBodyOf(Player player) => Body?.Player?.Pointer == player.Pointer;

    public override string ToString() => Name;

    // Creates the body asynchronously and fires OnReady when done
    public void Spawn(Vector3 floorPosition, Quaternion facing)
    {
        if (_spawning || Ready)
        {
            Logger.Info($"[Actor.SpawnSkipped] {this}: {(_spawning ? "already spawning" : "already exists")}");
            return;
        }

        if (BattlePlayer.Player?.Controller == null)
        {
            Logger.Info($"[Actor.SpawnSkipped] {this}: no local player to model it on");
            return;
        }

        _spawning = true;
        MelonCoroutines.Start(SpawnRoutine(floorPosition, facing));
    }

    public void Despawn()
    {
        if (Body is { } body)
        {
            PlayerManager.instance?.AllPlayers?.Remove(body.Player);
            if (body.Controller != null) UnityEngine.Object.Destroy(body.Controller.gameObject);
            Logger.Info($"[Actor.Despawned] {this}: {body.Player.Describe()}");
        }

        Forget();
    }

    public void ForgetSceneObjects()
    {
        if (Body is { } body) PlayerManager.instance?.AllPlayers?.Remove(body.Player);
        Forget();
    }

    public void Update()
    {
        if (!Ready) return;

        if (Body.Controller.controllerType != ControllerType.Local)
        {
            Body.Controller.controllerType = ControllerType.Local;
            Logger.Warning($"[Actor.ControllerTypeRestored] {this}: found Remote outside a guarded call - set back to Local");
        }

        Body.Footing.Update();
        Body.Clips.Update();
    }

    public void LateUpdate()
    {
        if (!Ready) return;

        foreach (var part in Body.HiddenUi)
        {
            // Hides the nameplate and health bars by scaleing to 0. NameBending can throw on an inactive plate.
            if (part != null && part.localScale != Vector3.zero) part.localScale = Vector3.zero;
        }
    }

    // Run actor moves directly via stack name (e.g. `Straight`)
    public bool RunMove(string name)
    {
        if (!Ready) return false;

        var stack = Body.Controller.PlayerProcessor.availableStacks.ToArray()
            .FirstOrDefault(s => string.Equals(s.CachedName, name, StringComparison.OrdinalIgnoreCase));

        if (stack == null)
        {
            Logger.Warning($"[Actor.UnknownMove] {this} has no move called {name}");
            return false;
        }

        Body.Controller.PlayerProcessor.Execute(stack);
        Logger.Debug($"[Actor.Move] {this}: {stack.CachedName}");
        return true;
    }

    public void FullHeal()
    {
        if (!Ready) return;

        var hp = Body.Player.Data.HealthPoints;
        if (hp >= MaxHealth) return;

        Body.Controller.PlayerHealth.SetHealth(MaxHealth, hp, false);
        Logger.Debug($"[Actor.FullHealth] {this}: {hp} -> {MaxHealth}");
    }

    private IEnumerator SpawnRoutine(Vector3 floorPosition, Quaternion facing)
    {
        Player? player = null;
        try
        {
            player = ActorSpawner.Create(this, floorPosition, facing);
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
            else Body = ActorSpawner.Setup(this, player, floorPosition);
        }
        catch (Exception e)
        {
            Logger.Error($"[Actor.SpawnFailed] {this}: {e}");
            PlayerManager.instance?.AllPlayers?.Remove(player);
            if (player.Controller != null) UnityEngine.Object.Destroy(player.Controller.gameObject);
        }

        if (Body is { } body && IsCurrent(body)) MelonCoroutines.Start(FinishWhenVisible(body));
        else _spawning = false;
    }

    private IEnumerator FinishWhenVisible(ActorBody body)
    {
        var start = Time.realtimeSinceStartup;

        while (IsCurrent(body) && !body.Controller.PlayerVisuals.VisualsAreReadyAndApplied && Time.realtimeSinceStartup - start < VisualsTimeoutSeconds)
        {
            yield return null;
        }

        _spawning = false;
        if (!IsCurrent(body)) yield break;

        try
        {
            ActorSpawner.Reveal(this, body);
            Ready = true;
            OnReady?.Invoke(this);
        }
        catch (Exception e)
        {
            Logger.Error($"[Actor.VisualsFailed] {this}: {e}");
        }
    }

    private bool IsCurrent(ActorBody body) => ReferenceEquals(Body, body) && body.Controller != null;

    private void Forget()
    {
        Body = null;
        Ready = false;
    }
}

internal record ActorBody(Player Player, PlayerController Controller, PlayerRig Rig, ActorPlacement Footing, ClipPlayer Clips, ActorCollisions Collisions, Transform[] HiddenUi);
