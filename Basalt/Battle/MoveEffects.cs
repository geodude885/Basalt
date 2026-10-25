using Il2CppRUMBLE.Audio;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using UnityEngine;

namespace Basalt.Battle;

internal class MoveEffects
{
    private const string PunchMove = "Straight";

    private static readonly Dictionary<string, string> KnownSpawns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Disc"] = "Disc",
        ["SpawnBall"] = "Ball",
        ["SpawnCube"] = "RockCube",
        ["SpawnPillar"] = "Pillar",
        ["SpawnWall"] = "Wall",
    };

    private Dictionary<string, Effects>? _spawns;
    private HashSet<string> _grounded = new(StringComparer.OrdinalIgnoreCase);
    private Effects? _punch;

    private record Effects(string Move, List<AudioCall> Sounds, List<string> Visuals);

    /// <summary>The spawn sound and effect for a structure, by pool name.</summary>
    public void Spawn(string structure, Vector3 floor, Quaternion facing)
    {
        if (!EnsureExists()) return;
        if (!_spawns!.TryGetValue(structure, out var effects))
        {
            // Scenery rocks have no spawn move: they sound like a ball.
            _spawns.TryGetValue("Ball", out effects);
        }

        Play(effects, floor, facing);
    }

    /// <summary>
    /// Whether the game spawns this structure out of the ground rather than in mid-air. The disc
    /// does, though its spawn step is not the grounded kind.
    /// </summary>
    public bool IsGrounded(string structure) =>
        string.Equals(structure, "Disc", StringComparison.OrdinalIgnoreCase) || (EnsureExists() && _grounded.Contains(structure));

    public void Punch(Vector3 at, Quaternion facing)
    {
        if (EnsureExists()) Play(_punch, at, facing);
    }

    public void Forget()
    {
        _spawns = null;
        _grounded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _punch = null;
    }

    private static void Play(Effects? effects, Vector3 at, Quaternion facing)
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
        if (_spawns != null) return true;

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
                var grounded = false;
                foreach (var process in stack.Processes.ToArray())
                {
                    if (process.StructureSpawnModifier is { } spawnStep)
                    {
                        var kind = spawnStep.GetIl2CppType().Name;
                        grounded |= kind.Contains("Grounded", StringComparison.Ordinal) && !kind.Contains("NonGrounded", StringComparison.Ordinal);
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
                if (spawned != null && grounded) _grounded.Add(spawned);
                if (string.Equals(stack.CachedName, PunchMove, StringComparison.OrdinalIgnoreCase)) _punch = effects;
            }

            _spawns = spawns;
            Logger.Debug($"[Effects.Found] spawns: {string.Join(", ", spawns.Select(s => $"{s.Key} ({s.Value.Move}: {s.Value.Sounds.Count} sound, {string.Join("/", s.Value.Visuals)}{(_grounded.Contains(s.Key) ? ", grounded" : "")})"))}; " +
                $"punch: {(_punch == null ? "none" : $"{_punch.Sounds.Count} sound, {string.Join("/", _punch.Visuals)}")}");
            return true;
        }
        catch (Exception e)
        {
            _spawns = [];
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
