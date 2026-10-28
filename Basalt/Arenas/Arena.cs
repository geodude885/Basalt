using Basalt.Presentation;
using Basalt.Services.Charts;
using UnityEngine;
using Part = Basalt.Arenas.ArenaVisuals.Part;

namespace Basalt.Arenas;

internal class Arena(GameAssets assets)
{
    public ArenaCollision Colliders { get; } = new();

    public ArenaVisuals Visuals { get; } = new(assets);

    public void Create(Anchor origin, Lane lane, bool showBackstop, bool walls = true, Zone? zone = null, LaneColours? colours = null, bool panes = false)
    {
        Destroy();
        colours ??= LaneColours.Default;
        BuildVisuals(origin, lane, colours, showBackstop, walls ? zone : null, walls && (panes || colours.ShowsWalls));
        if (walls) BuildColliders(origin, lane, zone);
        Logger.Debug($"[Lane.{(walls ? "Built" : "Preview")}] {lane.Length:F1} m x {lane.Width:F1} m at {origin.Position.ToUnity().Describe()}");
    }

    private void BuildColliders(Anchor origin, Lane lane, Zone? zone)
    {
        var along = Quaternion.LookRotation(origin.Forward.ToUnity());
        var layer = GameHelpers.LayerUnder(origin.Position.ToUnity());
        var across = lane.Width + (2f * (ArenaCollision.SideGap + ArenaCollision.Thickness));
        var ends = lane.Width + 2f + (2f * ArenaCollision.Thickness);
        var behind = ArenaCollision.EndGap + ArenaCollision.Thickness;
        var tall = ArenaCollision.Height + ArenaCollision.WallLengthThroughFloor;

        Vector3 At(float x, float z)
        {
            return origin.ToWorld(new System.Numerics.Vector3(x, (ArenaCollision.Height - ArenaCollision.WallLengthThroughFloor) / 2f, z)).ToUnity();
        }

        Colliders.Wall("Basalt_Backstop", At(0f, -ArenaCollision.EndGap - (ArenaCollision.Thickness / 2f)), along, new Vector3(ends, tall, ArenaCollision.Thickness), layer);
        Colliders.Wall("Basalt_WallFar", At(0f, lane.Length + ArenaCollision.EndGap + (ArenaCollision.Thickness / 2f)), along, new Vector3(ends, tall, ArenaCollision.Thickness), layer);

        // From behind the backstop to beyond the far wall, leaving no gap at the corners.
        var run = lane.Length + (2f * behind);
        var sideX = (lane.Width / 2f) + ArenaCollision.SideGap + (ArenaCollision.Thickness / 2f);
        Colliders.Wall("Basalt_WallLeft", At(-sideX, lane.Length / 2f), along, new Vector3(ArenaCollision.Thickness, tall, run), layer);
        Colliders.Wall("Basalt_WallRight", At(sideX, lane.Length / 2f), along, new Vector3(ArenaCollision.Thickness, tall, run), layer);

        if (zone is not { } playerZone) return;

        if (playerZone.Length < lane.Length - 0.01f)
            Colliders.Wall("Basalt_ZoneEnd", At(0f, playerZone.Length + ArenaCollision.PlayerEndGap + (ArenaCollision.Thickness / 2f)), along, new Vector3(across, tall, ArenaCollision.Thickness), layer, zone: true);

        var depth = playerZone.Length + behind + ArenaCollision.PlayerEndGap + ArenaCollision.Thickness;
        var ceiling = origin.ToWorld(new System.Numerics.Vector3(0f, playerZone.Height + (ArenaCollision.Thickness / 2f), (depth / 2f) - behind)).ToUnity();
        Colliders.Wall("Basalt_ZoneCeiling", ceiling, along, new Vector3(across, ArenaCollision.Thickness, depth), layer, zone: true);
    }

    private void BuildVisuals(Anchor origin, Lane lane, LaneColours colours, bool frameBackstop, Zone? zone, bool panes)
    {
        Visuals.Colour(colours);
        var along = Quaternion.LookRotation(origin.Forward.ToUnity());

        Vector3 At(float x, float y, float z)
        {
            return origin.ToWorld(new System.Numerics.Vector3(x, y, z)).ToUnity();
        }

        var halfWidth = lane.Width / 2f;

        Visuals.Line(At(-halfWidth, ArenaVisuals.LineHeight, lane.Length / 2f), along, new Vector3(ArenaVisuals.LineThickness, ArenaVisuals.LineHeight, lane.Length), Part.Edge);
        Visuals.Line(At(halfWidth, ArenaVisuals.LineHeight, lane.Length / 2f), along, new Vector3(ArenaVisuals.LineThickness, ArenaVisuals.LineHeight, lane.Length), Part.Edge);
        Visuals.Line(At(0f, ArenaVisuals.LineHeight, 0f), along, new Vector3(lane.Width, ArenaVisuals.LineHeight, ArenaVisuals.LineThickness), Part.End);
        Visuals.Line(At(0f, ArenaVisuals.LineHeight, lane.Length), along, new Vector3(lane.Width, ArenaVisuals.LineHeight, ArenaVisuals.LineThickness), Part.End);
        if (zone is { } playerZone && playerZone.Length < lane.Length - 0.01f)
            Visuals.Line(At(0f, ArenaVisuals.LineHeight, playerZone.Length), along, new Vector3(lane.Width, ArenaVisuals.LineHeight, ArenaVisuals.LineThickness), Part.Zone);

        if (frameBackstop)
        {
            var width = lane.Width + 2f;

            Visuals.Line(At(0f, ArenaVisuals.FrameHeight, -ArenaVisuals.FrameGap), along, new Vector3(width, ArenaVisuals.FrameThickness, ArenaVisuals.FrameThickness), Part.Frame);
            Visuals.Line(At(-width / 2f, ArenaVisuals.FrameHeight / 2f, -ArenaVisuals.FrameGap), along, new Vector3(ArenaVisuals.FrameThickness, ArenaVisuals.FrameHeight, ArenaVisuals.FrameThickness), Part.Frame);
            Visuals.Line(At(width / 2f, ArenaVisuals.FrameHeight / 2f, -ArenaVisuals.FrameGap), along, new Vector3(ArenaVisuals.FrameThickness, ArenaVisuals.FrameHeight, ArenaVisuals.FrameThickness), Part.Frame);
        }

        if (!panes) return;

        var height = zone?.Height ?? Zone.DefaultHeight;
        var paneX = halfWidth + ArenaCollision.SideGap;
        var near = -ArenaCollision.EndGap;
        var far = lane.Length + ArenaCollision.EndGap;
        var middle = (near + far) / 2f;

        Visuals.Pane(At(-paneX, height / 2f, middle), along, new Vector3(ArenaVisuals.PaneThickness, height, far - near));
        Visuals.Pane(At(paneX, height / 2f, middle), along, new Vector3(ArenaVisuals.PaneThickness, height, far - near));
        Visuals.Pane(At(0f, height / 2f, near), along, new Vector3(paneX * 2f, height, ArenaVisuals.PaneThickness));
        Visuals.Pane(At(0f, height / 2f, far), along, new Vector3(paneX * 2f, height, ArenaVisuals.PaneThickness));
        if (zone != null) Visuals.Pane(At(0f, height, middle), along, new Vector3(paneX * 2f, ArenaVisuals.PaneThickness, far - near));
    }

    public void Destroy()
    {
        Colliders.Destroy();
        Visuals.Destroy();
    }

    // Ensure use on scene change
    public void Forget()
    {
        Colliders.Forget();
        Visuals.Forget();
    }
}
