using UnityEngine;

namespace Basalt.DevTools.Flatscreen;

/// <summary>The plain keys that drive a player, yours or the test actor in bot control. Ctrl+ keys are the tools'.</summary>
internal static class FlatscreenKeys
{
    private static readonly (KeyCode Key, string Stack)[] Moves =
    {
        (KeyCode.Space, "Jump"),
        (KeyCode.LeftShift, "RockSlide"),
        (KeyCode.Alpha1, "SpawnCube"),
        (KeyCode.Alpha2, "SpawnPillar"),
        (KeyCode.Alpha3, "SpawnWall"),
        (KeyCode.Alpha4, "SpawnBall"),
        (KeyCode.Z, "Straight"),
        (KeyCode.X, "Uppercut"),
        (KeyCode.C, "Kick"),
        (KeyCode.V, "Stomp"),
        (KeyCode.T, "Disc"),
        (KeyCode.G, "Explode"),
    };

    public static bool CtrlHeld => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

    public static Vector2 Walk => CtrlHeld ? Vector2.zero : new Vector2(Axis(KeyCode.D, KeyCode.A), Axis(KeyCode.W, KeyCode.S));

    public static Vector2 Turn => CtrlHeld ? Vector2.zero : new Vector2(Axis(KeyCode.E, KeyCode.Q), 0f);

    /// <summary>The moves whose keys went down this frame.</summary>
    public static IEnumerable<string> PressedMoves => CtrlHeld ? [] : Moves.Where(m => Input.GetKeyDown(m.Key)).Select(m => m.Stack);

    private static float Axis(KeyCode positive, KeyCode negative) =>
        (Input.GetKey(positive) ? 1f : 0f) - (Input.GetKey(negative) ? 1f : 0f);
}
