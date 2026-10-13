using System.Collections;
using BossBattles.Actors;
using BossBattles.Services.Charts;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;
using MelonLoader;
using UnityEngine;

namespace BossBattles.Battle;

/// <summary>Laying out a battle: the lane, the board, you, and the actors on their spots.</summary>
internal sealed partial class BattleController
{
    /// <summary>A newly ready actor placed at once had not settled and went on falling.</summary>
    private const float ActorSettleSeconds = 0.1f;

    private IEnumerator PrepareAfterSettling(int session)
    {
        // The match maps finish setting themselves up over the first frames.
        yield return new WaitForSeconds(ArenaSettleSeconds);
        if (session == _session && State == BattleState.LoadingArena) Prepare();
    }

    private void Prepare()
    {
        try
        {
            PlayerController you = PlayerManager.instance.localPlayer.Controller;
            var yours = ActorRig.From(you);

            LaneFrame? frame = _fixedFrame ?? Arena.Frame(CurrentArena, _arenaSpec, Chart!.Lane, yours.Feet, yours.Facing);
            if (frame == null)
            {
                Stop("no lane could be laid out");
                return;
            }

            _frame = frame.Value;
            ClearStructures();

            // Reposition places the head, so the floor point is lifted by your eye height.
            var eyeHeight = yours.Head.position.y - yours.Vr.position.y;
            ActorSpec performer = Chart!.Actors.FirstOrDefault(a => a.Id == _recordActor) ?? Chart.DefaultActor;

            if (Mode == BattleMode.Record)
            {
                // In the actor's place, facing down the lane as it will; a section starts where it stood then.
                Vector3 spot = _recordSpot is { } at ? World(at.X, 0f, at.Y) : World(performer.At.X, 0f, performer.At.Z);
                PlaceYou(you, spot, Quaternion.Euler(0f, _frame.ActorYawDegrees(performer.At.Facing), 0f), eyeHeight);
            }
            else if (Arena.IsMatchMap(CurrentArena) || _arenaSpec.HasSpot || _fixedFrame != null)
            {
                PlaceYou(you, StringHelpers.ToUnity(_frame.Anchor.Position), Quaternion.LookRotation(StringHelpers.ToUnity(_frame.Anchor.Forward)), eyeHeight);
            }

            _lane.Build(_frame, Chart.Lane, ShowBackstop, walls: true, zone: Mode == BattleMode.Record ? null : Chart.Zone,
                colours: Chart.ColoursIn(Chart.Phases.LastOrDefault(p => p.Start <= StartAt)), panes: Chart.EverShowsWalls);
            if (Mode == BattleMode.Record)
            {
                _board.Place(World(performer.At.X, 2.6f, Math.Max(0f, performer.At.Z - 3f)), World(performer.At.X, 0f, performer.At.Z));
                _board.Show($"<b>Recording {performer.Name}</b>\nget ready");
            }
            else
            {
                _board.Place(World(0f, 2.6f, Math.Min(3f, Chart.Lane.Length / 2f)), StringHelpers.ToUnity(_frame.Anchor.Position));
                _board.Show($"<b>{Chart.Name}</b>\n{(Mode == BattleMode.Review ? "clip review" : "get ready")}");
            }

            foreach (ActorSpec spec in Mode == BattleMode.Record ? Enumerable.Empty<ActorSpec>() : Chart.Actors)
            {
                var actor = new Actor(spec.Id, spec.Name, spec.Look, spec.Shiftstones) { SelfKnockbackMultiplier = 0f, Measurement = spec.Measurement };
                _actors.Add(actor);
                Vector3 spot = World(spec.At.X, 0f, spec.At.Z);
                var yaw = _frame.ActorYawDegrees(spec.At.Facing);

                // Spawning puts the head on the floor, so the body falls until placed properly.
                var session = _session;
                actor.OnReady += ready => MelonCoroutines.Start(PlaceWhenSettled(ready, spot, yaw, session));
                actor.Spawn(spot, Quaternion.Euler(0f, yaw, 0f));
            }

            _spawnDeadline = Time.time + ActorSpawnTimeoutSeconds;
            State = BattleState.SpawningActors;
        }
        catch (Exception e)
        {
            Logger.Error($"[Battle.PrepareFailed] {e}");
            Stop("preparing failed");
        }
    }

    /// <summary>
    /// Moves you with the game's <c>Reposition</c>, then corrects once from where your feet landed:
    /// it places the head, and turning you swings the head's offset from the rig with it.
    /// </summary>
    private void PlaceYou(PlayerController you, Vector3 floor, Quaternion facing, float eyeHeight)
    {
        you.PlayerMovement.Reposition(floor + (Vector3.up * eyeHeight), facing);
        MelonCoroutines.Start(CorrectPlacement(you, floor, facing, eyeHeight, _session));
    }

    private IEnumerator CorrectPlacement(PlayerController you, Vector3 floor, Quaternion facing, float eyeHeight, int session)
    {
        yield return null;
        yield return null;
        if (session != _session || you == null) yield break;

        Vector3 feet = ActorRig.From(you).Feet;
        var off = new Vector3(floor.x - feet.x, 0f, floor.z - feet.z);
        if (off.magnitude > 0.15f)
        {
            you.PlayerMovement.Reposition(floor + off + (Vector3.up * eyeHeight), facing);
            yield return null;
            yield return null;
            if (session != _session || you == null) yield break;
            feet = ActorRig.From(you).Feet;
        }

        var left = new Vector2(floor.x - feet.x, floor.z - feet.z).magnitude;
        Logger.Write(left > 0.3f ? LogLevel.Warning : LogLevel.Debug, $"[{(left > 0.3f ? "Battle.YouPlacedOff" : "Battle.YouPlaced")}] {StringHelpers.Vector(feet)}, {left:0.00} m from {StringHelpers.Vector(floor)} (first try {off.magnitude:0.00} m off)");
    }

    private IEnumerator PlaceWhenSettled(Actor actor, Vector3 spot, float yaw, int session)
    {
        yield return new WaitForSecondsRealtime(ActorSettleSeconds);
        if (session == _session && actor.Exists && State is not (BattleState.Idle or BattleState.Running or BattleState.Finished)) actor.Place(spot, yaw);
    }
}
