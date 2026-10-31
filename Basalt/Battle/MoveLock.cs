using Basalt.Services.Charts.Model;
using Il2CppRUMBLE.Players;
using Stack = Il2CppRUMBLE.MoveSystem.Stack;

namespace Basalt.Battle;

internal class MoveLock
{
    private static readonly string[] SpawnMoves = { "Disc", "SpawnBall", "SpawnCube", "SpawnPillar", "SpawnWall" };

    private readonly List<Stack> _withheld = [];
    private PlayerController? _withheldFrom;
    private PlayerController? _heldFor;

    public void DisableMoves()
    {
        var playerController = BattlePlayer.Player?.Controller;
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

    public void AllowMoves(SpawnRule rule = SpawnRule.All)
    {
        var playerController = _heldFor;
        _heldFor = null;
        if (playerController == null) return;

        try
        {
            // Re-initialising rebuilds the move list from every move you own, so the rule clips it after.
            playerController.PlayerProcessor.InitializeStackInputSources(true);
            if (rule != SpawnRule.All) LimitMoves(playerController, rule);
            if (_withheld.Count > 0) RemoveLimits(playerController);
        }
        catch (Exception e)
        {
            Logger.Warning($"[Battle.MovesNotReleased] your moves may stay off until the scene changes: {e.Message}");
        }
    }

    public void Reset()
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

    public void Forget()
    {
        _heldFor = null;
        _withheldFrom = null;
        _withheld.Clear();
    }

    private void LimitMoves(PlayerController playerController, SpawnRule rule)
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

    private void RemoveLimits(PlayerController playerController)
    {
        var handles = playerController.PlayerProcessor.activeStackHandles;
        var poses = playerController.PlayerPoseSystem;

        foreach (var handle in handles.ToArray())
        {
            if (handle?.Stack == null || !_withheld.Any(s => s.Pointer == handle.Stack.Pointer || string.Equals(s.CachedName, handle.Stack.CachedName, StringComparison.OrdinalIgnoreCase))) continue;

            handles.Remove(handle);

            var source = handle.InputSource;
            var shared = source != null && handles.ToArray().Any(h => h?.InputSource != null && h.InputSource.Pointer == source.Pointer);
            if (source != null && !shared) poses.RemoveInputSource(source);
        }
    }
}
