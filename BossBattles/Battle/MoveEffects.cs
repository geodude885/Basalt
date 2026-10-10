using BossBattles.Core;
using Il2CppRUMBLE.Audio;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;
using UnityEngine;

namespace BossBattles.Battle;

/// <summary>
/// The game's own sounds and effects for spawning a structure and for punching one, read off a
/// player's moves rather than named here: each move is processes of modifiers, and the spawn moves
/// carry the structure they spawn (<c>SpawnStructureModifier.StructurePrefabName</c>), their spawn
/// sound (<c>PlayAudioModifer</c>) and effect (<c>PlayVFXModifier</c>). Chart projectiles play these
/// so they look and sound like a player's: rising with the spawn sound, flying with the punch.
/// </summary>
internal static class MoveEffects
{
    private const string PunchMove = "Straight";

    /// <summary>The spawn moves and what they spawn - used when a move's own data does not name its structure.</summary>
    private static readonly Dictionary<string, string> KnownSpawns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Disc"] = "Disc",
        ["SpawnBall"] = "Ball",
        ["SpawnCube"] = "RockCube",
        ["SpawnPillar"] = "Pillar",
        ["SpawnWall"] = "Wall",
    };

    private static Dictionary<string, Effects>? _spawns;
    private static HashSet<string> _grounded = new(StringComparer.OrdinalIgnoreCase);
    private static Effects? _punch;

    private sealed record Effects(string Move, List<AudioCall> Sounds, List<string> Visuals);

    /// <summary>The spawn sound and effect for a structure (by pool name), at the floor it rises from.</summary>
    public static void Spawn(string structure, Vector3 floor, Quaternion facing)
    {
        if (!Ensure()) return;
        if (!_spawns!.TryGetValue(structure, out Effects? effects))
        {
            // Rocks of the scenery have no spawn move: they sound like a spawned ball.
            _spawns.TryGetValue("Ball", out effects);
        }

        Play(effects, floor, facing);
    }

    /// <summary>
    /// Whether the game spawns this structure on the ground (its spawn move's spawn step is the
    /// grounded kind) - a disc or cube rests on the floor rather than appearing in mid-air.
    /// </summary>
    public static bool IsGrounded(string structure) =>
        string.Equals(structure, "Disc", StringComparison.OrdinalIgnoreCase) // comes up out of the ground in game, though its spawn step is not the grounded kind
        || (Ensure() && _grounded.Contains(structure));

    /// <summary>The punch's sound and effect, where a projectile is launched.</summary>
    public static void Punch(Vector3 at, Quaternion facing)
    {
        if (Ensure()) Play(_punch, at, facing);
    }

    /// <summary>For a new scene: what the moves pointed at may have gone with the old one.</summary>
    public static void Forget()
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
            AudioManager? audio = AudioManager.instance;
            if (audio != null)
            {
                foreach (AudioCall call in effects.Sounds.Where(c => c != null)) audio.Play(call, at, false);
            }

            foreach (var visual in effects.Visuals) PoolManager.instance?.GetPooledObject(visual, at, facing);
        }
        catch (Exception e)
        {
            Mod.Note("Effects.Failed", $"{effects.Move}: {e.Message}");
        }
    }

    private static bool Ensure()
    {
        if (_spawns != null) return true;

        PlayerController? you = PlayerManager.instance?.localPlayer?.Controller;
        if (you == null) return false;

        try
        {
            var spawns = new Dictionary<string, Effects>(StringComparer.OrdinalIgnoreCase);
            foreach (Stack stack in you.PlayerProcessor.availableStacks.ToArray())
            {
                if (stack == null) continue;

                string? spawned = null;
                var sounds = new List<AudioCall>();
                var visuals = new List<string>();
                var kinds = new List<string>();
                var grounded = false;
                foreach (Process process in stack.Processes.ToArray())
                {
                    if (process.StructureSpawnModifier is { } spawnStep)
                    {
                        var kind = spawnStep.GetIl2CppType().Name;
                        kinds.Add($"spawn:{kind}");
                        grounded |= kind.Contains("Grounded", StringComparison.Ordinal) && !kind.Contains("NonGrounded", StringComparison.Ordinal);
                        if (spawnStep.TryCast<SpawnStructureModifier>() is { } spawnModifier)
                        {
                            var name = !string.IsNullOrEmpty(spawnModifier.StructurePrefabName) ? spawnModifier.StructurePrefabName : spawnModifier.StructurePrefab?.name;
                            if (!string.IsNullOrEmpty(name)) spawned ??= name;
                        }
                    }

                    foreach (Binding binding in process.Bindings.ToArray())
                    {
                        kinds.Add(binding.GetIl2CppType().Name);
                        if (binding.TryCast<SpawnStructureModifier>() is { } spawn)
                        {
                            var name = !string.IsNullOrEmpty(spawn.StructurePrefabName) ? spawn.StructurePrefabName : spawn.StructurePrefab?.name;
                            if (!string.IsNullOrEmpty(name)) spawned ??= name;
                        }

                        if (binding.TryCast<PlayAudioModifer>() is { AudioCall: { } call }) sounds.Add(call);
                        if (binding.TryCast<PlayVFXModifier>() is { } vfx && !string.IsNullOrEmpty(vfx.VFXPrefabName)) visuals.Add(vfx.VFXPrefabName);
                    }
                }

                if (spawned == null && KnownSpawns.TryGetValue(stack.CachedName, out var known)) spawned = known;
                if (KnownSpawns.ContainsKey(stack.CachedName))
                    Mod.Note("Effects.Move", $"{stack.CachedName}: spawns {spawned ?? "?"}, steps {string.Join(", ", kinds)}", echo: false);

                var effects = new Effects(stack.CachedName, sounds, visuals);
                if (spawned != null) spawns.TryAdd(spawned, effects);
                if (spawned != null && grounded) _grounded.Add(spawned);
                if (string.Equals(stack.CachedName, PunchMove, StringComparison.OrdinalIgnoreCase)) _punch = effects;
            }

            _spawns = spawns;
            Mod.Note("Effects.Found",
                $"spawns: {string.Join(", ", spawns.Select(s => $"{s.Key} ({s.Value.Move}: {s.Value.Sounds.Count} sound, {string.Join("/", s.Value.Visuals)}{(_grounded.Contains(s.Key) ? ", grounded" : "")})"))}; " +
                $"punch: {(_punch == null ? "none" : $"{_punch.Sounds.Count} sound, {string.Join("/", _punch.Visuals)}")}", echo: false);
            return true;
        }
        catch (Exception e)
        {
            _spawns = [];
            Mod.Note("Effects.Failed", $"could not read the moves' effects: {e.Message}");
            return false;
        }
    }
}
