using BossBattles.Battle;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using UnityEngine;

namespace BossBattles.DevTools.Testing;

/// <summary>What the self-test reads off the game and the log, so the mod carries no hooks for it.</summary>
internal static partial class SelfTest
{
    private static readonly float[] LaneCheckHeights = { 0.3f, 1.2f };

    private static bool _keepPlayerAlive;

    private static Vector3? LaneOrigin => LanePoint(0f, 0f, 0f);

    private static float LaneMetres => Mod.Battle.Lane != null && Mod.Battle.Chart is { } chart ? chart.Lane.Length : 0f;

    /// <summary>The count from the last <c>[Battle.Cleared] n structure(s)</c> line.</summary>
    private static int LastCleared =>
        Lines.LastOrDefault(l => l.Line.StartsWith("[Battle.Cleared]", StringComparison.Ordinal)).Line is { } line && int.TryParse(line.Split(' ')[1], out var cleared) ? cleared : 0;

    private static PlayerController? You => BattlePlayer.Controller;

    private static Vector3? LanePoint(float x, float y, float z) =>
        Mod.Battle.Lane is { } frame ? frame.ToWorld(new System.Numerics.Vector3(x, y, z)).ToUnity() : null;

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
    private static Rigidbody? SlidingBody(Rigidbody? except = null) =>
        CombatManager.instance?.Structures.ToArray()
            .Where(s => s != null && s.gameObject.activeInHierarchy)
            .Select(s => s.GetComponent<Rigidbody>())
            .Where(b => b != null && b != except && !b.isKinematic && (b.constraints & RigidbodyConstraints.FreezePositionY) != 0)
            .MaxBy(b => b.linearVelocity.magnitude);

    /// <summary>Whether a lane line is painted this colour.</summary>
    private static bool LaneShows(Color colour) =>
        UnityEngine.Object.FindObjectsOfType<Renderer>()
            .Where(r => r.gameObject.name == "BossBattles_Lane" && r.sharedMaterial != null)
            .Select(r => r.sharedMaterial.HasProperty("_BaseColor") ? r.sharedMaterial.GetColor("_BaseColor") : r.sharedMaterial.color)
            .Any(c => Mathf.Abs(c.r - colour.r) < 0.02f && Mathf.Abs(c.g - colour.g) < 0.02f && Mathf.Abs(c.b - colour.b) < 0.02f);

    /// <summary>
    /// The nearest place around <paramref name="around"/> a lane lies flat and clear: the floor under its middle and
    /// both edges level with its start, from behind the backstop to past the far end, and nothing in the way at
    /// cube or chest height. The Gym's spawn is ringed by platforms and slopes that stall grounded cubes.
    /// </summary>
    private static (Vector3 Start, Vector3 Direction)? FlatLane(Vector3 around, float length, float width)
    {
        var directions = Enumerable.Range(0, 8).Select(i => Quaternion.Euler(0f, 45f * i, 0f) * Vector3.forward).ToArray();

        bool FlatAndClear(Vector3 start, Vector3 direction)
        {
            var right = Vector3.Cross(Vector3.up, direction);
            var level = Enumerable.Range(-2, (int)length + 4)
                .SelectMany(z => new[] { -(width / 2f) - 0.5f, 0f, (width / 2f) + 0.5f }.Select(x => start + (right * x) + (direction * z)))
                .All(point => GameHelpers.HeightUnder(point) is { } floor && Mathf.Abs(floor - start.y) < 0.08f);

            return level && LaneCheckHeights.All(height => !Physics.RaycastAll(start + (Vector3.up * height) - (direction * 2f), direction, length + 3f).Any(hit => hit.collider.ProbablyGround()));
        }

        return Enumerable.Range(-10, 21)
            .SelectMany(x => Enumerable.Range(-10, 21).Select(z => around + new Vector3(x * 4f, 0f, z * 4f)))
            .OrderBy(spot => (spot - around).sqrMagnitude)
            .Select(spot => GameHelpers.HeightUnder(spot) is { } floor ? new Vector3(spot.x, floor, spot.z) : (Vector3?)null)
            .OfType<Vector3>()
            .SelectMany(start => directions.Select(direction => (Start: start, Direction: direction)))
            .Where(lane => FlatAndClear(lane.Start, lane.Direction))
            .Select(lane => ((Vector3 Start, Vector3 Direction)?)lane)
            .FirstOrDefault();
    }

    /// <summary>Tops your health up while a long test chart plays, so it runs to the end.</summary>
    private static void KeepPlayerAlive()
    {
        if (!_keepPlayerAlive || BattlePlayer.Player is not { Controller: not null } playerController) return;

        var hp = playerController.Data.HealthPoints;
        if (hp < PlayerHealth.MAX_PLAYER_HEALTH) playerController.Controller.PlayerHealth.SetHealth((short)PlayerHealth.MAX_PLAYER_HEALTH, hp, false);
    }
}
