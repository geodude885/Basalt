using Basalt.Arenas;
using Basalt.Battle;
using Basalt.Services;
using Basalt.Services.Charts;
using UnityEngine;

namespace Basalt.Menu;

// Where the menu stands: near you as its screen asks, or at its saved spot in the Gym.
internal class MenuMover(JsonBook<SavedSpot> spots)
{
    // In front of you: metres ahead of your eyes, above them (negative is below) and to your right.
    private const float DistanceAhead = 0.75f;
    private const float HeightAboveEyes = -0.35f;
    private const float SidewaysToRight = 0f;

    // Behind you while miming, far enough back to be clear of your arms.
    private const float DistanceBehind = 1f;

    // A following menu is brought back once you are this far from it.
    private const float FollowDistance = 1.6f;

    public void PlaceOnOpening(Transform menu, MenuPlacement placement)
    {
        if (placement != MenuPlacement.InFront || !PlaceAtGymSpot(menu)) PlaceNearPlayer(menu, placement);
    }

    public static void PlaceNearPlayer(Transform menu, MenuPlacement placement)
    {
        if (BattlePlayer.Rig is not { } rig) return;

        var facing = rig.Facing;
        var right = Vector3.Cross(Vector3.up, facing);
        var position = placement switch
        {
            MenuPlacement.Beside => rig.Head.position + (facing * 0.35f) - (right * 0.8f) + (Vector3.down * 0.45f),
            MenuPlacement.Behind => rig.Head.position - (facing * DistanceBehind) + (Vector3.up * HeightAboveEyes),
            _ => rig.Head.position + (facing * DistanceAhead) + (Vector3.up * HeightAboveEyes) + (right * SidewaysToRight),
        };

        menu.SetPositionAndRotation(position, Quaternion.LookRotation((position - rig.Head.position).Flat()));
    }

    // True when it was brought back.
    public static bool FollowPlayer(Transform menu, MenuPlacement placement)
    {
        if (BattlePlayer.Rig is not { } rig) return false;

        var away = menu.position - rig.Head.position;
        if (new Vector2(away.x, away.z).magnitude <= FollowDistance) return false;

        PlaceNearPlayer(menu, placement);
        return true;
    }

    public void SaveGymSpot(Transform menu)
    {
        if (!ArenaKind.Gym.IsLoaded()) return;

        try
        {
            spots.Set(Maps.GymScene, new SavedSpot(menu.position.x, menu.position.y, menu.position.z, menu.eulerAngles.y));
            Logger.Debug($"[Menu.Moved] {menu.position.Describe()}");
        }
        catch (IOException e)
        {
            Logger.Error($"[Menu.SaveFailed] {e.Message}");
        }
    }

    private bool PlaceAtGymSpot(Transform menu)
    {
        if (!ArenaKind.Gym.IsLoaded() || spots.Get(Maps.GymScene) is not { } spot) return false;

        menu.SetPositionAndRotation(new Vector3(spot.X, spot.Y, spot.Z), Quaternion.Euler(0f, spot.Yaw, 0f));
        return true;
    }
}
