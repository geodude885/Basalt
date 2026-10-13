using BossBattles.Services.Charts;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppRUMBLE.Poses;
using Stack = Il2CppRUMBLE.MoveSystem.Stack;

namespace BossBattles.Battle;

/// <summary>Your moves: off through the countdown, and the spawn moves a chart's rule clips away.</summary>
internal sealed partial class BattleController
{
    private static readonly string[] SpawnMoves = { "Disc", "SpawnBall", "SpawnCube", "SpawnPillar", "SpawnWall" };

    private readonly List<Stack> _withheld = [];
    private PlayerController? _withheldFrom;
    private PlayerController? _movesHeldFor;

    /// <summary>Your gesture recognition off until GO, the same switch an actor's clip uses.</summary>
    private void HoldYourMoves()
    {
        PlayerController? you = PlayerManager.instance?.localPlayer?.Controller;
        if (you == null || _movesHeldFor != null) return;

        try
        {
            you.PlayerProcessor.RemoveAllStackInputSources();
            _movesHeldFor = you;
            Logger.Debug("[Battle.MovesHeld] until GO");
        }
        catch (Exception e)
        {
            Logger.Warning($"[Battle.MovesNotHeld] {e.Message}");
        }
    }

    /// <summary>Your moves back at GO, without the spawn moves the chart's rule disallows; miming keeps them off.</summary>
    private void ReleaseYourMoves()
    {
        if (Miming && State == BattleState.Countdown) return;

        PlayerController? you = _movesHeldFor;
        _movesHeldFor = null;
        if (you == null) return;

        try
        {
            if (State == BattleState.Countdown && Mode != BattleMode.Record && Chart != null && Chart.Rules.Spawn != SpawnRule.All) Withhold(you, Chart.Rules.Spawn);
            you.PlayerProcessor.InitializeStackInputSources(true);
            if (_withheld.Count > 0) DropWithheldGestures(you);
        }
        catch (Exception e)
        {
            Logger.Warning($"[Battle.MovesNotReleased] your moves may stay off until the scene changes: {e.Message}");
        }
    }

    private void Withhold(PlayerController you, SpawnRule rule)
    {
        Il2CppSystem.Collections.Generic.List<Stack> stacks = you.PlayerProcessor.availableStacks;
        _withheld.AddRange(stacks.ToArray().Where(stack =>
            stack != null
            && SpawnMoves.Contains(stack.CachedName, StringComparer.OrdinalIgnoreCase)
            && !(rule == SpawnRule.Discs && string.Equals(stack.CachedName, "Disc", StringComparison.OrdinalIgnoreCase))));
        foreach (Stack stack in _withheld) stacks.Remove(stack);

        _withheldFrom = you;
        Logger.Debug($"[Battle.SpawnsWithheld] {rule}: {string.Join(", ", _withheld.Select(s => s.CachedName))}");
    }

    /// <summary>
    /// Takes the withheld moves' gestures away too: gesture inputs are built from every move you own
    /// (<c>MoveManager.FetchOwnedMoves</c>), not <c>availableStacks</c>. Each handle goes, and its pose
    /// input unless an allowed move shares it.
    /// </summary>
    private void DropWithheldGestures(PlayerController you)
    {
        Il2CppSystem.Collections.Generic.List<PlayerStackProcessor.StackHandle> handles = you.PlayerProcessor.activeStackHandles;
        PlayerPoseSystem poses = you.PlayerPoseSystem;
        var dropped = new List<string>();

        foreach (PlayerStackProcessor.StackHandle? handle in handles.ToArray())
        {
            if (handle?.Stack == null || !_withheld.Any(s => s.Pointer == handle.Stack.Pointer || string.Equals(s.CachedName, handle.Stack.CachedName, StringComparison.OrdinalIgnoreCase))) continue;

            handles.Remove(handle);
            dropped.Add(handle.Stack.CachedName);

            PoseInputSource? source = handle.InputSource;
            var shared = source != null && handles.ToArray().Any(h => h?.InputSource != null && h.InputSource.Pointer == source.Pointer);
            if (source != null && !shared) poses.RemoveInputSource(source);
        }

        Logger.Write(dropped.Count > 0 ? LogLevel.Debug : LogLevel.Warning, $"[{(dropped.Count > 0 ? "Battle.GesturesDropped" : "Battle.NoGesturesFound")}] {(dropped.Count > 0 ? string.Join(", ", dropped) : "no gesture handles for the withheld moves")}; {handles.Count} gesture(s) left");
    }

    private void GiveSpawnsBack()
    {
        PlayerController? you = _withheldFrom;
        _withheldFrom = null;
        if (you == null || _withheld.Count == 0)
        {
            _withheld.Clear();
            return;
        }

        try
        {
            you.PlayerProcessor.RemoveAllStackInputSources();
            foreach (Stack stack in _withheld) you.PlayerProcessor.availableStacks.Add(stack);
            you.PlayerProcessor.InitializeStackInputSources(true);
            Logger.Debug($"[Battle.SpawnsReturned] {string.Join(", ", _withheld.Select(s => s.CachedName))}");
        }
        catch (Exception e)
        {
            Logger.Warning($"[Battle.SpawnsNotReturned] your spawn moves may stay off until the scene changes: {e.Message}");
        }

        _withheld.Clear();
    }
}
