using BossBattles.Battle;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using UnityEngine;

namespace BossBattles.DevTools.Testing;

/// <summary>What the self-test reads off the game and the log, so the mod carries no hooks for it.</summary>
internal static partial class SelfTest
{
    private static bool _keepYouAlive;

    private static Vector3? LaneOrigin => LanePoint(0f, 0f, 0f);

    private static float LaneMetres => Mod.Battle.Lane is { } frame && Mod.Battle.Chart is { } chart ? chart.Lane.Length * frame.DepthScale : 0f;

    /// <summary>The count from the last <c>[Battle.Cleared] n structure(s)</c> line.</summary>
    private static int LastCleared =>
        Lines.LastOrDefault(l => l.Line.StartsWith("[Battle.Cleared]", StringComparison.Ordinal)).Line is { } line && int.TryParse(line.Split(' ')[1], out var cleared) ? cleared : 0;

    private static PlayerController? You => PlayerManager.instance?.localPlayer?.Controller;

    private static Vector3? LanePoint(float x, float y, float z) =>
        Mod.Battle.Lane is { } frame ? StringHelpers.ToUnity(frame.ToWorld(new System.Numerics.Vector3(x, y, z))) : null;

    /// <summary>Up behind you over the backstop, looking down the lane.</summary>
    private static void ShowOverview()
    {
        if (Mod.Battle.Chart is not { } chart || LanePoint(1.5f, 3.6f, -0.4f) is not { } position || LanePoint(0f, 0.8f, chart.Lane.Length * 0.6f) is not { } lookAt) return;
        _showCamera?.Invoke(position, lookAt);
    }

    private static int GroundedLaunches =>
        Lines.Count(l => l.Line.StartsWith("[Projectile.Launched]", StringComparison.Ordinal) && l.Line.Contains("grounded along", StringComparison.Ordinal));

    private static bool Available(string move) =>
        You?.PlayerProcessor.availableStacks.ToArray().Any(s => string.Equals(s?.CachedName, move, StringComparison.OrdinalIgnoreCase)) ?? false;

    private static bool HasGesture(string move) =>
        You?.PlayerProcessor.activeStackHandles.ToArray().Any(h => string.Equals(h?.Stack?.CachedName, move, StringComparison.OrdinalIgnoreCase)) ?? false;

    /// <summary>The fastest structure held to the floor by a grounded slide.</summary>
    private static Rigidbody? SlidingBody() =>
        CombatManager.instance?.Structures.ToArray()
            .Where(s => s != null && s.gameObject.activeInHierarchy)
            .Select(s => s.GetComponent<Rigidbody>())
            .Where(b => b != null && !b.isKinematic && (b.constraints & RigidbodyConstraints.FreezePositionY) != 0)
            .MaxBy(b => b.linearVelocity.magnitude);

    /// <summary>Whether a lane line is painted this colour.</summary>
    private static bool LaneShows(Color colour) =>
        UnityEngine.Object.FindObjectsOfType<Renderer>()
            .Where(r => r.gameObject.name == "BossBattles_Lane" && r.sharedMaterial != null)
            .Select(r => r.sharedMaterial.HasProperty("_BaseColor") ? r.sharedMaterial.GetColor("_BaseColor") : r.sharedMaterial.color)
            .Any(c => Mathf.Abs(c.r - colour.r) < 0.02f && Mathf.Abs(c.g - colour.g) < 0.02f && Mathf.Abs(c.b - colour.b) < 0.02f);

    /// <summary>Of 16 directions, the one with the most clear space at chest height.</summary>
    private static (Vector3 Direction, float Clear) OpenestDirection(Vector3 floor, float reach = 30f)
    {
        Vector3 from = floor + (Vector3.up * 1.2f);
        return Enumerable.Range(0, 16)
            .Select(i => Quaternion.Euler(0f, 22.5f * i, 0f) * Vector3.forward)
            .Select(direction => (Direction: direction, Clear: Physics.RaycastAll(from, direction, reach)
                .Where(hit => Arena.IsGround(hit.collider))
                .Select(hit => hit.distance)
                .DefaultIfEmpty(reach)
                .Min()))
            .MaxBy(option => option.Clear);
    }

    /// <summary>Tops your health up while a long test chart plays, so it runs to the end.</summary>
    private static void KeepYouAlive()
    {
        if (!_keepYouAlive || PlayerManager.instance?.localPlayer is not { Controller: not null } you) return;

        var hp = you.Data.HealthPoints;
        if (hp < PlayerHealth.MAX_PLAYER_HEALTH) you.Controller.PlayerHealth.SetHealth((short)PlayerHealth.MAX_PLAYER_HEALTH, hp, false);
    }
}
