using BossBattles.Services.Charts;
using UnityEngine;
using Part = BossBattles.Arena.Visuals.Part;

namespace BossBattles.Arena;

internal class Arena
{
    public Boundary Boundary { get; } = new();

    public Visuals Visuals { get; } = new();

    public void Build(Anchor origin, Lane lane, bool showBackstop, bool walls = true, Zone? zone = null, LaneColours? colours = null, bool panes = false)
    {
        Destroy();
        colours ??= LaneColours.Default;
        BuildVisuals(origin, lane, colours, showBackstop, walls ? zone : null, walls && (panes || colours.ShowsWalls));
        if (walls) BuildBoundary(origin, lane, zone);
        Logger.Debug($"[Lane.{(walls ? "Built" : "Preview")}] {lane.Length:F1} m x {lane.Width:F1} m at {origin.Position.ToUnity().Describe()}");
    }

    private void BuildBoundary(Anchor origin, Lane lane, Zone? zone)
    {
        var along = Quaternion.LookRotation(origin.Forward.ToUnity());
        var layer = GameHelpers.LayerUnder(origin.Position.ToUnity());
        var across = lane.Width + (2f * (Boundary.SideGap + Boundary.Thickness));
        var ends = lane.Width + 2f + (2f * Boundary.Thickness);
        var behind = Boundary.EndGap + Boundary.Thickness;

        Vector3 At(float x, float z)
        {
            return origin.ToWorld(new System.Numerics.Vector3(x, Boundary.Height / 2f, z)).ToUnity();
        }

        Boundary.Wall("BossBattles_Backstop", At(0f, -Boundary.EndGap - (Boundary.Thickness / 2f)), along, new Vector3(ends, Boundary.Height, Boundary.Thickness), layer);
        Boundary.Wall("BossBattles_WallFar", At(0f, lane.Length + Boundary.EndGap + (Boundary.Thickness / 2f)), along, new Vector3(ends, Boundary.Height, Boundary.Thickness), layer);

        // From behind the backstop to beyond the far wall, leaving no gap at the corners.
        var run = lane.Length + (2f * behind);
        var sideX = (lane.Width / 2f) + Boundary.SideGap + (Boundary.Thickness / 2f);
        Boundary.Wall("BossBattles_WallLeft", At(-sideX, lane.Length / 2f), along, new Vector3(Boundary.Thickness, Boundary.Height, run), layer);
        Boundary.Wall("BossBattles_WallRight", At(sideX, lane.Length / 2f), along, new Vector3(Boundary.Thickness, Boundary.Height, run), layer);

        if (zone is not { } yours) return;

        if (yours.Length < lane.Length - 0.01f)
            Boundary.Wall("BossBattles_ZoneEnd", At(0f, yours.Length + Boundary.PlayerEndGap + (Boundary.Thickness / 2f)), along, new Vector3(across, Boundary.Height, Boundary.Thickness), layer, zone: true);

        var depth = yours.Length + behind + Boundary.PlayerEndGap + Boundary.Thickness;
        Vector3 ceiling = origin.ToWorld(new System.Numerics.Vector3(0f, yours.Height + (Boundary.Thickness / 2f), (depth / 2f) - behind)).ToUnity();
        Boundary.Wall("BossBattles_ZoneCeiling", ceiling, along, new Vector3(across, Boundary.Thickness, depth), layer, zone: true);
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

        Visuals.Line(At(-halfWidth, Visuals.LineHeight, lane.Length / 2f), along, new Vector3(Visuals.LineThickness, Visuals.LineHeight, lane.Length), Part.Edge);
        Visuals.Line(At(halfWidth, Visuals.LineHeight, lane.Length / 2f), along, new Vector3(Visuals.LineThickness, Visuals.LineHeight, lane.Length), Part.Edge);
        Visuals.Line(At(0f, Visuals.LineHeight, 0f), along, new Vector3(lane.Width, Visuals.LineHeight, Visuals.LineThickness), Part.End);
        Visuals.Line(At(0f, Visuals.LineHeight, lane.Length), along, new Vector3(lane.Width, Visuals.LineHeight, Visuals.LineThickness), Part.End);
        if (zone is { } yours && yours.Length < lane.Length - 0.01f)
            Visuals.Line(At(0f, Visuals.LineHeight, yours.Length), along, new Vector3(lane.Width, Visuals.LineHeight, Visuals.LineThickness), Part.Zone);

        if (frameBackstop)
        {
            var width = lane.Width + 2f;

            Visuals.Line(At(0f, Visuals.FrameHeight, -Visuals.FrameGap), along, new Vector3(width, Visuals.FrameThickness, Visuals.FrameThickness), Part.Frame);
            Visuals.Line(At(-width / 2f, Visuals.FrameHeight / 2f, -Visuals.FrameGap), along, new Vector3(Visuals.FrameThickness, Visuals.FrameHeight, Visuals.FrameThickness), Part.Frame);
            Visuals.Line(At(width / 2f, Visuals.FrameHeight / 2f, -Visuals.FrameGap), along, new Vector3(Visuals.FrameThickness, Visuals.FrameHeight, Visuals.FrameThickness), Part.Frame);
        }

        if (!panes) return;

        var height = zone?.Height ?? Zone.DefaultHeight;
        var paneX = halfWidth + Boundary.SideGap;
        var near = -Boundary.EndGap;
        var far = lane.Length + Boundary.EndGap;
        var middle = (near + far) / 2f;

        Visuals.Pane(At(-paneX, height / 2f, middle), along, new Vector3(Visuals.PaneThickness, height, far - near));
        Visuals.Pane(At(paneX, height / 2f, middle), along, new Vector3(Visuals.PaneThickness, height, far - near));
        Visuals.Pane(At(0f, height / 2f, near), along, new Vector3(paneX * 2f, height, Visuals.PaneThickness));
        Visuals.Pane(At(0f, height / 2f, far), along, new Vector3(paneX * 2f, height, Visuals.PaneThickness));
        if (zone != null) Visuals.Pane(At(0f, height, middle), along, new Vector3(paneX * 2f, Visuals.PaneThickness, far - near));
    }

    public void Destroy()
    {
        Boundary.Destroy();
        Visuals.Destroy();
    }

    // Ensure use on scene change
    public void Forget()
    {
        Boundary.Forget();
        Visuals.Forget();
    }
}
