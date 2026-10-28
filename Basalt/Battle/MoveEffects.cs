using Il2CppRUMBLE.Audio;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using UnityEngine;

namespace Basalt.Battle;

internal class MoveEffects
{
    private const string HitMove = "Straight";

    private static readonly Dictionary<string, string> KnownSpawns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Disc"] = "Disc",
        ["SpawnBall"] = "Ball",
        ["SpawnCube"] = "RockCube",
        ["SpawnPillar"] = "Pillar",
        ["SpawnWall"] = "Wall",
    };

    private Dictionary<string, Effects>? _spawnEffects;
    private Effects? _hitEffect;

    private record Effects(string Move, List<AudioCall> Sounds, List<string> Visuals);

    public void PlaySpawnEffect(string structure, Vector3 floor, Quaternion facing)
    {
        if (!EnsureExists()) return;

        // Default to ball effect
        if (!_spawnEffects!.TryGetValue(structure, out var effects))
            _spawnEffects.TryGetValue("Ball", out effects);

        PlayEffect(effects, floor, facing);
    }
    public void PlayStraightEffect(Vector3 at, Quaternion facing)
    {
        if (EnsureExists()) PlayEffect(_hitEffect, at, facing);
    }

    public void Forget()
    {
        _spawnEffects = null;
        _hitEffect = null;
    }

    private static void PlayEffect(Effects? effects, Vector3 at, Quaternion facing)
    {
        if (effects == null) return;

        try
        {
            var audio = AudioManager.instance;
            if (audio != null)
            {
                foreach (var call in effects.Sounds.Where(c => c != null)) audio.Play(call, at, false);
            }

            foreach (var visual in effects.Visuals) PoolManager.instance?.GetPooledObject(visual, at, facing);
        }
        catch (Exception e)
        {
            Logger.Error($"[Effects.Failed] {effects.Move}: {e.Message}");
        }
    }

    private bool EnsureExists()
    {
        if (_spawnEffects != null) return true;

        return InitialiseEffects();
    }

    private bool InitialiseEffects()
    {
        var playerController = BattlePlayer.Player?.Controller;
        if (playerController == null) return false;

        try
        {
            var spawns = new Dictionary<string, Effects>(StringComparer.OrdinalIgnoreCase);
            foreach (var stack in playerController.PlayerProcessor.availableStacks.ToArray())
            {
                if (stack == null) continue;

                string? spawned = null;
                var sounds = new List<AudioCall>();
                var visuals = new List<string>();
                foreach (var process in stack.Processes.ToArray())
                {
                    if (process.StructureSpawnModifier is { } spawnStep)
                    {
                        spawned ??= PrefabName(spawnStep.TryCast<SpawnStructureModifier>());
                    }

                    foreach (var binding in process.Bindings.ToArray())
                    {
                        spawned ??= PrefabName(binding.TryCast<SpawnStructureModifier>());

                        if (binding.TryCast<PlayAudioModifer>() is { AudioCall: { } call }) sounds.Add(call);
                        if (binding.TryCast<PlayVFXModifier>() is { } vfx && !string.IsNullOrEmpty(vfx.VFXPrefabName)) visuals.Add(vfx.VFXPrefabName);
                    }
                }

                if (spawned == null && KnownSpawns.TryGetValue(stack.CachedName, out var known)) spawned = known;

                var effects = new Effects(stack.CachedName, sounds, visuals);
                if (spawned != null) spawns.TryAdd(spawned, effects);
                if (string.Equals(stack.CachedName, HitMove, StringComparison.OrdinalIgnoreCase)) _hitEffect = effects;
            }

            _spawnEffects = spawns;
            Logger.Debug($"[Effects.Found] spawns: {string.Join(", ", spawns.Select(s => $"{s.Key} ({s.Value.Move}: {s.Value.Sounds.Count} sound, {string.Join("/", s.Value.Visuals)})"))}; " +
                $"punch: {(_hitEffect == null ? "none" : $"{_hitEffect.Sounds.Count} sound, {string.Join("/", _hitEffect.Visuals)}")}");
            return true;
        }
        catch (Exception e)
        {
            _spawnEffects = [];
            Logger.Error($"[Effects.Failed] could not read the moves' effects: {e.Message}");
            return false;
        }
    }

    private static string? PrefabName(SpawnStructureModifier? spawn)
    {
        var name = spawn == null ? null : !string.IsNullOrEmpty(spawn.StructurePrefabName) ? spawn.StructurePrefabName : spawn.StructurePrefab?.name;
        return string.IsNullOrEmpty(name) ? null : name;
    }
}
