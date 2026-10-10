using BossBattles.Core;
using BossBattles.Services.Charts;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppRUMBLE.Poses;
using Stack = Il2CppRUMBLE.MoveSystem.Stack;

namespace BossBattles.Battle;

/// <summary>Your moves: off through the countdown, and the spawn moves a chart's rule takes away.</summary>
internal sealed partial class BattleController
{
    private static readonly string[] SpawnMoves = { "Disc", "SpawnBall", "SpawnCube", "SpawnPillar", "SpawnWall" };

    private readonly List<Stack> _withheld = [];
    private PlayerController? _withheldFrom;
    private PlayerController? _movesHeldFor;

    /// <summary>Gestures for withheld moves still live after dropping them; 0 when right.</summary>
    public int WithheldGesturesLeft { get; private set; }

    /// <summary>Whether your gestures include a move right now.</summary>
    public static bool HasGesture(string move)
    {
        PlayerController? you = PlayerManager.instance?.localPlayer?.Controller;
        return you != null && you.PlayerProcessor.activeStackHandles.ToArray().Any(h => string.Equals(h?.Stack?.CachedName, move, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether a spawn move is off your list right now.</summary>
    public bool Withholds(string move) => _withheld.Any(s => string.Equals(s.CachedName, move, StringComparison.OrdinalIgnoreCase));

    /// <summary>Your gesture recognition off until GO, the same switch an actor's take uses.</summary>
    private void HoldYourMoves()
    {
        PlayerController? you = PlayerManager.instance?.localPlayer?.Controller;
        if (you == null || _movesHeldFor != null) return;

        try
        {
            you.PlayerProcessor.RemoveAllStackInputSources();
            _movesHeldFor = you;
            Mod.Note("Battle.MovesHeld", "until GO", echo: false);
        }
        catch (Exception e)
        {
            Mod.Note("Battle.MovesNotHeld", e.Message);
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
            Mod.Note("Battle.MovesNotReleased", $"your moves may stay off until the scene changes: {e.Message}");
        }
    }

    private void Withhold(PlayerController you, SpawnRule rule)
    {
        Il2CppSystem.Collections.Generic.List<Stack> stacks = you.PlayerProcessor.availableStacks;
        foreach (Stack stack in stacks.ToArray())
        {
            if (stack == null || !SpawnMoves.Contains(stack.CachedName, StringComparer.OrdinalIgnoreCase)) continue;
            if (rule == SpawnRule.Discs && string.Equals(stack.CachedName, "Disc", StringComparison.OrdinalIgnoreCase)) continue;

            stacks.Remove(stack);
            _withheld.Add(stack);
        }

        _withheldFrom = you;
        Mod.Note("Battle.SpawnsWithheld", $"{rule}: {string.Join(", ", _withheld.Select(s => s.CachedName))}", echo: false);
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

        WithheldGesturesLeft = handles.ToArray().Count(h => h?.Stack != null && _withheld.Any(s => string.Equals(s.CachedName, h.Stack.CachedName, StringComparison.OrdinalIgnoreCase)));
        Mod.Note(dropped.Count > 0 ? "Battle.GesturesDropped" : "Battle.NoGesturesFound", $"{(dropped.Count > 0 ? string.Join(", ", dropped) : "no gesture handles for the withheld moves")}; {handles.Count} gesture(s) left", echo: dropped.Count == 0);
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
            Mod.Note("Battle.SpawnsReturned", string.Join(", ", _withheld.Select(s => s.CachedName)), echo: false);
        }
        catch (Exception e)
        {
            Mod.Note("Battle.SpawnsNotReturned", $"your spawn moves may stay off until the scene changes: {e.Message}");
        }

        _withheld.Clear();
    }
}
