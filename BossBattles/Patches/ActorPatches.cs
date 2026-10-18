using BossBattles.Actors;
using BossBattles.Battle;
using BossBattles.Services.Clips;
using HarmonyLib;
using Il2CppRUMBLE.Audio;
using Il2CppRUMBLE.Combat.ShiftStones;
using Il2CppRUMBLE.Input;
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
    // Harmony only calls static methods, so what the patches use is handed over here, once.
    private static ActorRegistry _actors = null!;
    private static BattleController _battle = null!;

    internal static int Apply(HarmonyLib.Harmony harmony, ActorRegistry actors, BattleController battle)
    {
        _actors = actors;
        _battle = battle;
        return PatchGroup.Apply(harmony, typeof(ActorPatches));
    }

    /// <summary>
    /// An actor never reaches 0 HP, where the game's death handling would run - for an extra player
    /// it crashed the process. Before a lethal hit it is put back on full health, so the hit lands as
    /// any other: its effects play and the score counts all of it.
    /// </summary>
    [HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.ReduceHealth))]
    public static class NoLethalHits
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PlayerHealth __instance, short amount)
        {
            var runOriginal = true;

            PatchGuard.Run(nameof(NoLethalHits), () =>
            {
                var actor = _actors.Find(__instance.ParentController);
                if (actor == null || amount < actor.Player!.Data.HealthPoints) return;

                actor.FullHeal();
                if (amount < Actor.MaxHealth) return;

                // Lethal even on full health: scored, not dealt.
                runOriginal = false;
                _battle.OnActorDamaged(amount);
            });

            return runOriginal;
        }
    }

    [HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.SetHealth))]
    public static class NoLethalSetHealth
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PlayerHealth __instance, short newHealth)
        {
            var runOriginal = true;

            PatchGuard.Run(nameof(NoLethalSetHealth), () =>
            {
                if (newHealth <= 0 && _actors.Find(__instance.ParentController) != null) runOriginal = false;
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
                var controller = __instance.ParentController;
                if (controller == null || _actors.Find(controller)?.Exists != true) return;
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
                var controller = __instance.ParentController;
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

            var controller = __instance.ParentController;
            if (controller == null) return;

            var actor = _actors.Find(controller);
            if (actor != null)
            {
                _battle.OnActorDamaged(previousHealth - newHealth);
                return;
            }

            var you = BattlePlayer.Player;
            if (you != null && controller.assignedPlayer != null && controller.assignedPlayer.Pointer == you.Pointer)
                _battle.OnPlayerDamaged(previousHealth - newHealth, newHealth);
        });
    }

    /// <summary>An actor clips <see cref="Actor.SelfKnockbackMultiplier"/> of the game's knockback; 0 skips it.</summary>
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
                var actor = _actors.Find(controller);
                if (actor == null) return;

                if (actor.SelfKnockbackMultiplier <= 0f)
                {
                    runOriginal = false;
                    return;
                }

                multiplier *= actor.SelfKnockbackMultiplier;
            });

            knockbackMultiplier = multiplier;
            return runOriginal;
        }
    }

    /// <summary>
    /// A Local player's ProcessInput reads your joystick; an actor gets still sticks instead, as its clip
    /// moves and turns it. Gestures need nothing: pose recognition is pure head and hand geometry.
    /// </summary>
    [HarmonyPatch(typeof(PlayerMovement), nameof(PlayerMovement.ProcessInput))]
    public static class ActorSticks
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PlayerMovement __instance)
        {
            var runOriginal = true;

            PatchGuard.Run(nameof(ActorSticks), () =>
            {
                if (_actors.Find(__instance.ParentController) == null) return;

                runOriginal = false;
                __instance.currentRawWalkInput = Vector2.zero;
                __instance.currentRawTurnInput = Vector2.zero;
                __instance.currentWalkInput = Vector2.zero;
            });

            return runOriginal;
        }
    }

    // Sounds only your own player plays are authored 2D; played inside these scopes for an actor,
    // they are made positional (ActorSoundsPositional).

    /// <summary>A Local player's fingers follow your controllers; an actor's follow its clip instead.</summary>
    [HarmonyPatch(typeof(PlayerHandPresence), nameof(PlayerHandPresence.GetHandPresenceInputForHand))]
    public static class ActorFingers
    {
        private static bool _throwLogged;

        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PlayerHandPresence __instance, InputManager.Hand hand, ref PlayerHandPresence.HandPresenceInput __result)
        {
            ClipFingers? fingers = null;
            PatchGuard.Run(nameof(ActorFingers), () => fingers = _actors.Find(__instance.ParentController)?.Fingers(hand));
            if (fingers is not { } held) return true;

            __result = new PlayerHandPresence.HandPresenceInput { indexInput = held.Index, gripInput = held.Grip, thumbInput = held.Thumb, swayInput = held.Sway };
            return false;
        }

        /// <summary>
        /// Without a headset the game's own read throws every frame, and once patched the interop
        /// trampoline reports each one: suppressed (relaxed fingers), the first logged.
        /// </summary>
        public static Exception? Finalizer(Exception? __exception)
        {
            if (__exception == null || _throwLogged) return null;

            _throwLogged = true;
            Logger.Debug($"[Actor.FingersThrew] PlayerHandPresence.GetHandPresenceInputForHand throws - expected without a headset; suppressed: {__exception.Message}");
            return null;
        }
    }

    [HarmonyPatch(typeof(PlayerPoseSystem), nameof(PlayerPoseSystem.OnPoseCompleted))]
    public static class PoseSoundScope
    {
        public static void Prefix(PlayerPoseSystem __instance, out bool __state) => __state = ActorSoundScope.Enter(_actors, __instance.ParentController);

        public static void Postfix(bool __state) => ActorSoundScope.Exit(__state);
    }

    [HarmonyPatch(typeof(PlayerPoseSystem), nameof(PlayerPoseSystem.OnPoseSetCompleted))]
    public static class PoseSetSoundScope
    {
        public static void Prefix(PlayerPoseSystem __instance, out bool __state) => __state = ActorSoundScope.Enter(_actors, __instance.ParentController);

        public static void Postfix(bool __state) => ActorSoundScope.Exit(__state);
    }

    [HarmonyPatch(typeof(PlayerMovement), nameof(PlayerMovement.OnBecameGrounded))]
    public static class LandingSoundScope
    {
        public static void Prefix(PlayerMovement __instance, out bool __state) => __state = ActorSoundScope.Enter(_actors, __instance.ParentController);

        public static void Postfix(bool __state) => ActorSoundScope.Exit(__state);
    }

    [HarmonyPatch(typeof(PlayerShiftstoneSystem), nameof(PlayerShiftstoneSystem.ActivateUseShiftstoneEffects))]
    public static class ShiftstoneSoundScope
    {
        public static void Prefix(PlayerShiftstoneSystem __instance, out bool __state) => __state = ActorSoundScope.Enter(_actors, __instance.ParentController);

        public static void Postfix(bool __state) => ActorSoundScope.Exit(__state);
    }

    [HarmonyPatch(typeof(AudioManager), nameof(AudioManager.Play), new[] { typeof(AudioCall), typeof(Vector3), typeof(bool) })]
    public static class ActorSoundsPositional
    {
        public static void Postfix(AudioCall call, PooledAudioSource __result) => PatchGuard.Run(nameof(ActorSoundsPositional), () =>
        {
            if (!ActorSoundScope.Active || __result == null) return;

            var source = __result.AudioSource;
            if (source == null || source.spatialBlend >= 1f) return;

            Logger.Debug($"[Actor.SoundMadePositional] {(call == null ? "?" : call.name)} spatialBlend {source.spatialBlend:F2} -> 1");
            source.spatialBlend = 1f;
        });
    }
}
