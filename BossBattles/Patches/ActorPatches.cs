using BossBattles.Actors;
using BossBattles.Core;
using HarmonyLib;
using Il2CppRUMBLE.Audio;
using Il2CppRUMBLE.Combat.ShiftStones;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppRUMBLE.Pools;
using UnityEngine;

namespace BossBattles.Patches;

/// <summary>
/// What makes an actor behave as a player that is not you. Each patch acts on actors only; your own
/// player runs the game's code untouched (<see cref="HealthWatch"/> only reads). Skipping prefixes fail open.
/// </summary>
public static class ActorPatches
{
    public static int Apply(HarmonyLib.Harmony harmony) => PatchGroup.Apply(harmony, typeof(ActorPatches));

    /// <summary>Lethal damage knocks an actor out instead of running the game's death handling.</summary>
    [HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.ReduceHealth))]
    public static class KnockOutInsteadOfLethalReduce
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PlayerHealth __instance, short amount)
        {
            var runOriginal = true;

            PatchGuard.Run(nameof(KnockOutInsteadOfLethalReduce), () =>
            {
                Actor? actor = Mod.Actors.Find(__instance.ParentController);
                if (actor == null) return;

                if (actor.KnockedOut)
                {
                    runOriginal = false;
                    return;
                }

                var hp = actor.Player!.Data.HealthPoints;
                if (amount < hp) return;

                runOriginal = false;
                Mod.Battle.OnActorDamaged(actor, hp);
                actor.KnockOut($"hit of {amount} on {hp} HP");
            });

            return runOriginal;
        }
    }

    [HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.SetHealth))]
    public static class KnockOutInsteadOfLethalSet
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PlayerHealth __instance, short newHealth)
        {
            var runOriginal = true;

            PatchGuard.Run(nameof(KnockOutInsteadOfLethalSet), () =>
            {
                if (newHealth > 0) return;

                Actor? actor = Mod.Actors.Find(__instance.ParentController);
                if (actor == null) return;

                runOriginal = false;
                actor.KnockOut($"SetHealth({newHealth})");
            });

            return runOriginal;
        }
    }

    /// <summary>
    /// SetHealth on a Local player plays the 2D "you were hit" sound and can raise the change over
    /// Photon, so an actor is Remote for the length of the call.
    /// </summary>
    [HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.SetHealth))]
    public static class ActorHealthAsRemote
    {
        [HarmonyPriority(Priority.Last)]
        public static void Prefix(PlayerHealth __instance, out bool __state)
        {
            var flipped = false;

            PatchGuard.Run(nameof(ActorHealthAsRemote), () =>
            {
                PlayerController? controller = __instance.ParentController;
                if (controller == null || Mod.Actors.Find(controller)?.Exists != true) return;
                if (controller.controllerType != ControllerType.Local) return;

                controller.controllerType = ControllerType.Remote;
                flipped = true;
            });

            __state = flipped;
        }

        public static void Postfix(PlayerHealth __instance, bool __state)
        {
            if (!__state) return;

            PatchGuard.Run(nameof(ActorHealthAsRemote), () =>
            {
                PlayerController? controller = __instance.ParentController;
                controller?.controllerType = ControllerType.Local;
            });
        }
    }

    /// <summary>Reads every health change, yours and the actors', for the battle's score.</summary>
    [HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.SetHealth))]
    public static class HealthWatch
    {
        public static void Postfix(PlayerHealth __instance, short newHealth, short previousHealth) => PatchGuard.Run(nameof(HealthWatch), () =>
        {
            if (newHealth >= previousHealth) return;

            PlayerController? controller = __instance.ParentController;
            if (controller == null) return;

            Actor? actor = Mod.Actors.Find(controller);
            if (actor != null)
            {
                Mod.Battle.OnActorDamaged(actor, previousHealth - newHealth);
                return;
            }

            Player? you = PlayerManager.instance?.localPlayer;
            if (you != null && controller.assignedPlayer != null && controller.assignedPlayer.Pointer == you.Pointer)
                Mod.Battle.OnPlayerDamaged(previousHealth - newHealth, newHealth);
        });
    }

    /// <summary>An actor takes <see cref="Actor.KnockbackTaken"/> of the game's knockback; 0 skips it.</summary>
    [HarmonyPatch(typeof(CombatManager), nameof(CombatManager.ApplyPlayerKnockbackForce))]
    public static class Knockback
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PlayerController controller, ref float knockbackMultiplier)
        {
            var runOriginal = true;
            var multiplier = knockbackMultiplier;

            PatchGuard.Run(nameof(Knockback), () =>
            {
                Actor? actor = Mod.Actors.Find(controller);
                if (actor == null) return;

                if (actor.KnockbackTaken <= 0f)
                {
                    runOriginal = false;
                    return;
                }

                multiplier *= actor.KnockbackTaken;
            });

            knockbackMultiplier = multiplier;
            return runOriginal;
        }
    }

    /// <summary>
    /// A Local player's ProcessInput reads your joystick; a bot-controlled actor gets its own sticks
    /// instead. Gestures need nothing: pose recognition is pure head and hand geometry.
    /// </summary>
    [HarmonyPatch(typeof(PlayerMovement), nameof(PlayerMovement.ProcessInput))]
    public static class BotSticks
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PlayerMovement __instance)
        {
            var runOriginal = true;

            PatchGuard.Run(nameof(BotSticks), () =>
            {
                Actor? actor = Mod.Actors.Find(__instance.ParentController);
                if (actor == null || !actor.IsBotControlled) return;

                runOriginal = false;
                MovementInput.Apply(__instance, actor.Input.Walk, actor.Input.Turn);
            });

            return runOriginal;
        }

        /// <summary>
        /// Without a headset the game's ProcessInput throws every frame, and once patched the interop
        /// trampoline reports each one: log the first, count the rest.
        /// </summary>
        public static Exception? Finalizer(Exception? __exception)
        {
            if (__exception == null) return null;

            Mod.Note("Input.ProcessInputThrew",
                $"PlayerMovement.ProcessInput threw - expected without a headset; suppressed, further ones only counted: {__exception.Message}",
                echo: Mod.Census["Input.ProcessInputThrew"] == 0);
            return null;
        }
    }

    // Sounds only your own player plays are authored 2D; played inside these scopes for an actor,
    // they are made positional (ActorSoundsPositional).

    [HarmonyPatch(typeof(PlayerPoseSystem), nameof(PlayerPoseSystem.OnPoseCompleted))]
    public static class PoseSoundScope
    {
        public static void Prefix(PlayerPoseSystem __instance, out bool __state) => __state = ActorSoundScope.Enter(__instance.ParentController);

        public static void Postfix(bool __state) => ActorSoundScope.Exit(__state);
    }

    [HarmonyPatch(typeof(PlayerPoseSystem), nameof(PlayerPoseSystem.OnPoseSetCompleted))]
    public static class PoseSetSoundScope
    {
        public static void Prefix(PlayerPoseSystem __instance, out bool __state) => __state = ActorSoundScope.Enter(__instance.ParentController);

        public static void Postfix(bool __state) => ActorSoundScope.Exit(__state);
    }

    [HarmonyPatch(typeof(PlayerMovement), nameof(PlayerMovement.OnBecameGrounded))]
    public static class LandingSoundScope
    {
        public static void Prefix(PlayerMovement __instance, out bool __state) => __state = ActorSoundScope.Enter(__instance.ParentController);

        public static void Postfix(bool __state) => ActorSoundScope.Exit(__state);
    }

    [HarmonyPatch(typeof(PlayerShiftstoneSystem), nameof(PlayerShiftstoneSystem.ActivateUseShiftstoneEffects))]
    public static class ShiftstoneSoundScope
    {
        public static void Prefix(PlayerShiftstoneSystem __instance, out bool __state) => __state = ActorSoundScope.Enter(__instance.ParentController);

        public static void Postfix(bool __state) => ActorSoundScope.Exit(__state);
    }

    [HarmonyPatch(typeof(AudioManager), nameof(AudioManager.Play), new[] { typeof(AudioCall), typeof(Vector3), typeof(bool) })]
    public static class ActorSoundsPositional
    {
        public static void Postfix(AudioCall call, PooledAudioSource __result) => PatchGuard.Run(nameof(ActorSoundsPositional), () =>
        {
            if (!ActorSoundScope.Active || __result == null) return;

            AudioSource? source = __result.AudioSource;
            if (source == null || source.spatialBlend >= 1f) return;

            Mod.Note("Actor.SoundMadePositional", $"{(call == null ? "?" : call.name)} spatialBlend {source.spatialBlend:F2} -> 1", echo: false);
            source.spatialBlend = 1f;
        });
    }
}
