using Basalt.Services.Charts;
using Il2CppRUMBLE.Players;
using Stack = Il2CppRUMBLE.MoveSystem.Stack;

namespace Basalt.Battle;

/// <summary>Your moves in a battle: off through the countdown, and the spawn moves a chart's rule clips away.</summary>
internal class MoveLock
{
    private static readonly string[] SpawnMoves = { "Disc", "SpawnBall", "SpawnCube", "SpawnPillar", "SpawnWall" };

    private readonly List<Stack> _withheld = [];
    private PlayerController? _withheldFrom;
    private PlayerController? _heldFor;

    /// <summary>Your gesture recognition off, the same switch an actor's clip uses.</summary>
    public void Hold()
    {
        var playerController = BattlePlayer.Controller;
        if (playerController == null || _heldFor != null) return;

        try
        {
            playerController.PlayerProcessor.RemoveAllStackInputSources();
            _heldFor = playerController;
            Logger.Debug("[Battle.MovesHeld] until GO");
        }
        catch (Exception e)
        {
            Logger.Warning($"[Battle.MovesNotHeld] {e.Message}");
        }
    }

    /// <summary>Your moves back, without the spawn moves <paramref name="rule"/> disallows.</summary>
    public void Release(SpawnRule rule = SpawnRule.All)
    {
        var playerController = _heldFor;
        _heldFor = null;
        if (playerController == null) return;

        try
        {
            // Re-initialising rebuilds the move list from every move you own, so the rule clips it after.
            playerController.PlayerProcessor.InitializeStackInputSources(true);
            if (rule != SpawnRule.All) Withhold(playerController, rule);
            if (_withheld.Count > 0) DropWithheldGestures(playerController);
        }
        catch (Exception e)
        {
            Logger.Warning($"[Battle.MovesNotReleased] your moves may stay off until the scene changes: {e.Message}");
        }
    }

    public void GiveSpawnsBack()
    {
        var playerController = _withheldFrom;
        _withheldFrom = null;
        if (playerController == null || _withheld.Count == 0)
        {
            _withheld.Clear();
            return;
        }

        try
        {
            playerController.PlayerProcessor.RemoveAllStackInputSources();
            foreach (var stack in _withheld) playerController.PlayerProcessor.availableStacks.Add(stack);
            playerController.PlayerProcessor.InitializeStackInputSources(true);
            Logger.Debug($"[Battle.SpawnsReturned] {string.Join(", ", _withheld.Select(s => s.CachedName))}");
        }
        catch (Exception e)
        {
            Logger.Warning($"[Battle.SpawnsNotReturned] your spawn moves may stay off until the scene changes: {e.Message}");
        }

        _withheld.Clear();
    }

    /// <summary>For a scene change: your player went with the scene, its moves with it.</summary>
    public void Forget()
    {
        _heldFor = null;
        _withheldFrom = null;
        _withheld.Clear();
    }

    private void Withhold(PlayerController playerController, SpawnRule rule)
    {
        var stacks = playerController.PlayerProcessor.availableStacks;
        _withheld.AddRange(stacks.ToArray().Where(stack =>
            stack != null
            && SpawnMoves.Contains(stack.CachedName, StringComparer.OrdinalIgnoreCase)
            && !(rule == SpawnRule.Discs && string.Equals(stack.CachedName, "Disc", StringComparison.OrdinalIgnoreCase))));
        foreach (var stack in _withheld) stacks.Remove(stack);

        _withheldFrom = playerController;
        Logger.Debug($"[Battle.SpawnsWithheld] {rule}: {string.Join(", ", _withheld.Select(s => s.CachedName))}");
    }

    /// <summary>
    /// Takes the withheld moves' gestures away too: gesture inputs are built from every move you own
    /// (<c>MoveManager.FetchOwnedMoves</c>), not <c>availableStacks</c>. Each handle goes, and its pose
    /// input unless an allowed move shares it.
    /// </summary>
    private void DropWithheldGestures(PlayerController playerController)
    {
        var handles = playerController.PlayerProcessor.activeStackHandles;
        var poses = playerController.PlayerPoseSystem;
        var dropped = new List<string>();

        foreach (var handle in handles.ToArray())
        {
            if (handle?.Stack == null || !_withheld.Any(s => s.Pointer == handle.Stack.Pointer || string.Equals(s.CachedName, handle.Stack.CachedName, StringComparison.OrdinalIgnoreCase))) continue;

            handles.Remove(handle);
            dropped.Add(handle.Stack.CachedName);

            var source = handle.InputSource;
            var shared = source != null && handles.ToArray().Any(h => h?.InputSource != null && h.InputSource.Pointer == source.Pointer);
            if (source != null && !shared) poses.RemoveInputSource(source);
        }

        Logger.Write(dropped.Count > 0 ? LogLevel.Debug : LogLevel.Warning, $"[{(dropped.Count > 0 ? "Battle.GesturesDropped" : "Battle.NoGesturesFound")}] {(dropped.Count > 0 ? string.Join(", ", dropped) : "no gesture handles for the withheld moves")}; {handles.Count} gesture(s) left");
    }
}
