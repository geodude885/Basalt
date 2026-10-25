using Il2CppRUMBLE.Players;

namespace Basalt.Actors;

internal static class ActorLook
{
    // The chart's look for an actor: yours, one of the game's defaults, or a look string
    public static PlayerVisualData Resolve(Actor actor, PlayerVisualData playerLook)
    {
        var yours = PlayerVisualData.FromPlayfabDataString(playerLook.ToPlayfabDataString());

        switch (actor.PlayerCustomisation.Trim().ToLowerInvariant())
        {
            case "":
            case "you":
                return yours;
            case "default":
                return PlayerVisualData.Default;
            case "defaultmasc":
            case "defaultmale":
                return PlayerVisualData.DefaultMale;
            case "defaultfem":
            case "defaultfemale":
                return PlayerVisualData.DefaultFemale;
        }

        try
        {
            return PlayerVisualData.FromPlayfabDataString(actor.PlayerCustomisation);
        }
        catch (Exception e)
        {
            Logger.Warning($"[Actor.LookInvalid] {actor}: \"{actor.PlayerCustomisation}\" is not a look the game can read ({e.Message}) - using yours");
            return yours;
        }
    }
}
