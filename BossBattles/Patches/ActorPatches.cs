using BossBattles.Actors;
using BossBattles.Core;
using BossBattles.Flatscreen;
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
/// Everything that makes an actor behave as a player that is not you, keyed by
/// <see cref="ActorRegistry.Find(Player?)"/> so each patch touches actors only - your own player runs
/// the game's code untouched (apart from <see cref="HealthWatch"/>, which only reads).
/// Each body runs through <see cref="PatchGuard"/>; skipping prefixes fail open.
/// </summary>
public static class ActorPatches
{
    public static int Apply(HarmonyLib.Harmony harmony) => PatchGroup.Apply(harmony, typeof(ActorPatches));

    // ---- Health ----------------------------------------------------------------------------------

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
    /// SetHealth on a Local player plays the non-positional "you were hit" sound and can raise the
    /// change over Photon; an actor is Local, so for the length of the call it is Remote.
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
                if (controller == null || Mod.Actors.Find(controller)?.IsSimulated != true) return;
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

    /// <summary>Observes every health change - yours and the actors' - for the battle's score. Read-only.</summary>
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

    // ---- Knockback -------------------------------------------------------------------------------

    /// <summary>
    /// A simulated actor takes <see cref="Actor.KnockbackTaken"/> of the game's knockback (0 skips
    /// it). A puppet cannot be moved by it at all, so it gets its own slide.
    /// </summary>
    [HarmonyPatch(typeof(CombatManager), nameof(CombatManager.ApplyPlayerKnockbackForce))]
    public static class Knockback
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PlayerController controller, int tierPoints, Vector3 direction, ref float knockbackMultiplier, float forceModifier)
        {
            var runOriginal = true;
            var multiplier = knockbackMultiplier;

            PatchGuard.Run(nameof(Knockback), () =>
            {
                Actor? actor = Mod.Actors.Find(controller);
                if (actor == null) return;

                if (actor.IsPuppet) // PUPPET
                {
                    runOriginal = false;
                    actor.ApplyPuppetKnockback(direction.normalized * (tierPoints * Actor.PuppetKnockbackScale * multiplier * forceModifier));
                    return;
                }

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

    // ---- Input -----------------------------------------------------------------------------------

    /// <summary>
    /// A Local player's ProcessInput reads the global input actions - your joystick. For a
    /// bot-controlled actor it is replaced with the actor's own sticks, through the game's own
    /// formula (<see cref="MovementInput"/>). Gestures need nothing here: pose recognition is pure
    /// head/hand geometry.
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
        /// Without a headset the game's own ProcessInput throws every frame for your player; once
        /// patched, the interop trampoline reports each one. Suppress, log the first, count the rest.
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

    // ---- Sounds ----------------------------------------------------------------------------------
    // Some of a player's own sounds are authored 2D because only your own player runs those paths.
    // While one runs for an actor, sounds it plays are made positional; every other sound is left
    // as authored. See ActorSoundScope.

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
