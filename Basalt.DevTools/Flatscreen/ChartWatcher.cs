using Basalt.Battle;
using UnityEngine;

namespace Basalt.DevTools.Flatscreen;

// Watch mode, for checking a chart you are making without a headset: each battle is shown on the desktop from up
// behind the lane, with you kept on full health. Turning it on plays the chosen chart; between battles the view stays,
// so the menu can be clicked to play it again after a save. Recording is left alone.
internal sealed class ChartWatcher(DesktopCamera camera)
{
    public bool On { get; private set; }

    // A battle being watched, not a recording.
    public bool Watching => On && Mod.Battle.Run is { } run && run.Request.Mode != BattleMode.Record;

    public void Toggle()
    {
        On = !On;
        if (!On)
        {
            camera.Hide();
            Logger.Info("[Watch] off");
            return;
        }

        if (Mod.Battle.Run == null) Mod.Charts.StartBattle();
        Logger.Info("[Watch] on - battles show from above the lane, and you are kept on full health; Ctrl+W again to stop");
    }

    public void Update()
    {
        if (Watching && Overview() is { } view) camera.ShowFixed(view.Position, view.LookAt);
    }

    // Up behind you over the backstop, looking down the lane.
    public static (Vector3 Position, Vector3 LookAt)? Overview()
    {
        if (Mod.Battle.Run is not { Lane: { } lane } run) return null;

        Vector3 At(float x, float y, float z)
        {
            return lane.ToWorld(new System.Numerics.Vector3(x, y, z)).ToUnity();
        }

        return (At(1.5f, 3.6f, -0.4f), At(0f, 0.8f, run.Request.Chart.Lane.Length * 0.6f));
    }
}
