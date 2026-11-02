using System.Collections;
using Basalt.DevTools.Flatscreen;
using Basalt.Menu;
using Il2CppRUMBLE.Interactions.InteractionBase;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;
using UnityEngine;

namespace Basalt.DevTools.Testing;

internal static partial class SelfTest
{
    private const float PushTimeoutSeconds = 3f;

    private static int ActiveStructures => CombatManager.instance.Structures.ToArray().Count(s => s != null && s.gameObject.activeInHierarchy);

    /// <summary>The mouse turns your view and walking follows it, a click on what you look at pushes it, and the move keys' route runs moves.</summary>
    private static IEnumerator RunFlatscreenTest(List<string> problems)
    {
        if (!FlatscreenControls.IsFlatscreen)
        {
            problems.Add("needs to run without a headset");
            yield break;
        }

        if (You is not { } controller)
        {
            problems.Add("no local player");
            yield break;
        }

        yield return CheckLook(controller, problems);
        yield return CheckClick(controller, problems);
        yield return CheckMoves(controller, problems);
    }

    private static IEnumerator CheckLook(PlayerController controller, List<string> problems)
    {
        // A Gym just reloaded turns your rig back to the spawn's facing, once, a moment after it hands you over.
        yield return new WaitForSeconds(1f);

        var rig = PlayerRig.From(controller);
        var wanted = Quaternion.AngleAxis(60f, Vector3.up) * rig.Facing;
        MouseLook.Turn(rig, new Vector2(60f, 0f));
        yield return new WaitForSeconds(0.2f);

        var off = Vector3.Angle(rig.Facing, wanted);
        if (off > 1f) problems.Add($"a 60 degree turn ended {off:0.0} degrees off");

        var from = rig.Feet;
        FlatscreenControls.ScriptedWalk = new Vector2(0f, 1f);
        // A Gym just reloaded holds your movement for about 3 s.
        var deadline = Time.realtimeSinceStartup + 6f;
        while (Vector3.Distance(rig.Feet, from) < 1f && Time.realtimeSinceStartup < deadline) yield return null;

        FlatscreenControls.ScriptedWalk = null;
        yield return new WaitForSeconds(0.3f);

        var walked = rig.Feet - from;
        walked.y = 0f;
        Logger.Info($"[SelfTest] looked 60 degrees and walked {walked.magnitude:0.00} m, {Vector3.Angle(walked, wanted):0.0} degrees from the view");
        if (walked.magnitude < 0.5f || Vector3.Angle(walked, wanted) > 15f) problems.Add($"walked {walked.magnitude:0.0} m, {Vector3.Angle(walked, wanted):0} degrees off where you look");
    }

    private static IEnumerator CheckClick(PlayerController controller, List<string> problems)
    {
        var deadline = Time.realtimeSinceStartup + 5f;
        while (BasaltMod.Menu.Root?.activeSelf != true && Time.realtimeSinceStartup < deadline) yield return null;
        yield return new WaitForSeconds(1.2f);

        var button = BasaltMod.Menu.ButtonRow?.Buttons.FirstOrDefault(b => b.Spec.Id == "next");
        if (button?.Root.GetComponentInChildren<InteractionButton>(true) is not { } interaction)
        {
            problems.Add("the menu had no game NEXT button");
            yield break;
        }

        var presses = 0;
        interaction.onPressed.AddListener((Action)(() => presses++));

        // Close by, as you would walk up to it: from its Gym spot, across the map, its other buttons overlap this one.
        var menu = BasaltMod.Menu.Root!.transform;
        (var menuAt, var menuTurned) = (menu.position, menu.rotation);
        MenuMover.PlaceNearPlayer(menu, MenuPlacement.InFront);
        yield return new WaitForSeconds(0.5f);

        // What the crosshair would do: look at the button, then a ray out of the camera.
        var head = PlayerRig.From(controller).Head;
        head.rotation = Quaternion.LookRotation(interaction.PressPosition - head.position);
        yield return null;

        var before = CardText;
        var camera = Camera.main;
        var ray = camera == null ? default : camera.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f));
        var found = ButtonPusher.ButtonOn(ray);
        Logger.Info($"[SelfTest] the ray found {(found == null ? "no button" : found.name)} at {(found == null ? "-" : found.transform.position.Describe())}, NEXT at {interaction.PressPosition.Describe()}");
        if (found != interaction) problems.Add("the click's ray found another button than the one looked at");

        if (camera == null || !FlatscreenControls.Click(ray))
        {
            problems.Add($"the click found no button from {head.position.Describe()} to the NEXT button at {interaction.PressPosition.Describe()}");
            head.localRotation = Quaternion.identity;
            menu.SetPositionAndRotation(menuAt, menuTurned);
            yield break;
        }

        deadline = Time.realtimeSinceStartup + PushTimeoutSeconds;
        while (FlatscreenControls.Pushing && Time.realtimeSinceStartup < deadline) yield return null;
        yield return new WaitForSeconds(0.3f);
        head.localRotation = Quaternion.identity;

        Logger.Info($"[SelfTest] click: {presses} press(es), pressed {interaction.IsPressed}, reaching hands {interaction.PreInteractingHands?.Count}");
        if (presses != 1) problems.Add($"the click pressed NEXT {presses} times");
        if (before == CardText) problems.Add("the click did not change the picker's card");
        if (FlatscreenControls.Pushing || interaction.IsPressed || interaction.PreInteractingHands?.Count > 0) problems.Add("the button was not released after the click");
        BasaltMod.Menu.ButtonRow?.Press("prev");
        menu.SetPositionAndRotation(menuAt, menuTurned);
    }

    private static IEnumerator CheckMoves(PlayerController controller, List<string> problems)
    {
        var rig = PlayerRig.From(controller);
        var structures = ActiveStructures;
        if (!FlatscreenControls.RunMove(controller, "SpawnCube")) problems.Add("the SpawnCube key's move did not run");
        yield return new WaitForSeconds(1f);
        Logger.Info($"[SelfTest] structures {structures} -> {ActiveStructures} after SpawnCube");
        if (ActiveStructures <= structures) problems.Add($"SpawnCube left {ActiveStructures} active structures, from {structures}");

        var floor = rig.Feet.y;
        var top = floor;
        if (!FlatscreenControls.RunMove(controller, "Jump")) problems.Add("the Jump key's move did not run");
        for (var t = 0f; t < 1f; t += Time.deltaTime)
        {
            top = Mathf.Max(top, rig.Feet.y);
            yield return null;
        }

        Logger.Info($"[SelfTest] jumped {top - floor:0.00} m");
        if (top - floor < 0.5f) problems.Add($"the jump rose {top - floor:0.0} m");

        var nothing = FlatscreenControls.RunMove(controller, "NoSuchMove");
        if (nothing) problems.Add("a move you do not have ran");
    }
}
