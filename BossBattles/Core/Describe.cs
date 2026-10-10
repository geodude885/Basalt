using Il2CppRUMBLE.Players;
using UnityEngine;

namespace BossBattles.Core;

/// <summary>One-line descriptions of game objects for the log.</summary>
internal static class Describe
{
    public static string Player(Player? player)
    {
        if (player == null) return "null-player";

        var name = player.Data?.GeneralData?.PublicUsername ?? "?";
        return $"{name}(hp={player.Data?.HealthPoints})";
    }

    public static string Vector(Vector3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";

    public static System.Numerics.Vector3 ToNumerics(Vector3 v) => new(v.x, v.y, v.z);

    public static Vector3 ToUnity(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
}
