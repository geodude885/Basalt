using Il2CppRUMBLE.Combat.ShiftStones;
using Il2CppRUMBLE.Players.Subsystems;
using UnityEngine;

namespace BossBattles.Flatscreen;

/// <summary>
/// Feeds stick values to a player in place of <c>PlayerMovement.ProcessInput</c>, which reads the
/// global input actions (the VR controllers). Same formula as the game's own (read from the ISIL),
/// so sprinting and the WalkingSpeed / SprintingSpeed shiftstone multipliers still apply, and the
/// game's own Move / ProcessTurnInput do the rest:
/// <code>
/// raw  = stick, zeroed when activeFeatures lacks Movement (walk) / SnapTurn (turn)
/// walk = raw * WalkingSpeed + normalize(raw) * SprintingSpeed * currentSprintFactor
/// </code>
/// Call from a ProcessInput prefix that then skips the original.
/// </summary>
public static class MovementInput
{
    // PlayerMovement.Features: Movement = 1, SnapTurn = 2.
    private const int MovementFeature = 1;
    private const int SnapTurnFeature = 2;

    /// <param name="movement">The player's movement subsystem.</param>
    /// <param name="walk">-1..1 per axis; y is forward, relative to where the player's head faces.</param>
    /// <param name="turn">-1..1 on x; the game snap-turns on it.</param>
    public static void Apply(PlayerMovement movement, Vector2 walk, Vector2 turn)
    {
        var features = (int)movement.activeFeatures;
        if ((features & MovementFeature) == 0) walk = Vector2.zero;
        if ((features & SnapTurnFeature) == 0) turn = Vector2.zero;

        movement.currentRawWalkInput = walk;
        movement.currentRawTurnInput = turn;

        PlayerShiftstoneSystem? stones = movement.ParentController?.PlayerShiftstones;
        var walking = stones == null ? 1f : stones.GetTotalMultiplier(PlayerMultiplier.MultiplierType.WalkingSpeed, 1f, true, null);
        var sprinting = stones == null ? 1f : stones.GetTotalMultiplier(PlayerMultiplier.MultiplierType.SprintingSpeed, 1f, true, null);

        movement.currentWalkInput = (walk * walking) + (walk.normalized * (sprinting * movement.currentSprintFactor));
    }
}
