using BossBattles.Battle;
using BossBattles.Core;
using BossBattles.Services.Charts;
using BossBattles.Services.Scoring;
using BossBattles.UI;
using UnityEngine;

namespace BossBattles;

/// <summary>What the chart picker shows: the chosen chart between battles, and the studio's controls while recording.</summary>
public sealed partial class ModEntry
{
    private static readonly Color Orange = new(1f, 0.45f, 0.1f);
    private static readonly Color Green = new(0.2f, 0.75f, 0.3f);
    private static readonly Color Red = new(0.85f, 0.2f, 0.2f);
    private static readonly Color Blue = new(0.25f, 0.5f, 0.95f);

    private bool PanelWanted() => Mod.Studio.State switch
    {
        StudioState.Recording => Mod.Battle.State == BattleState.Running,
        StudioState.Setup or StudioState.ChoosingSection => true,
        StudioState.Reviewing or StudioState.PreviewingSection => false,
        StudioState.Deciding => Mod.Battle.State is BattleState.Idle or BattleState.Finished,
        _ => Picker.Open && Mod.Battle.State is BattleState.Idle or BattleState.Finished,
    };

    private IReadOnlyList<PickerButton> PanelButtons()
    {
        Studio studio = Mod.Studio;
        switch (studio.State)
        {
            case StudioState.Recording:
                return new[] { new PickerButton("stop", "STOP", Red, studio.Stop) };

            case StudioState.Deciding:
                return new[]
                {
                    new PickerButton("keep", "KEEP", Green, KeepTake),
                    new PickerButton("again", "AGAIN", Orange, studio.Again),
                    new PickerButton("replay", "REPLAY", Blue, studio.Review),
                    new PickerButton("discard", "DISCARD", Red, studio.Discard),
                };

            case StudioState.Setup:
                return new[]
                {
                    new PickerButton("longer", "LONGER", Orange, () => studio.Resize(1f, 0f)),
                    new PickerButton("shorter", "SHORTER", Orange, () => studio.Resize(-1f, 0f)),
                    new PickerButton("wider", "WIDER", Orange, () => studio.Resize(0f, 0.5f)),
                    new PickerButton("narrower", "NARROWER", Orange, () => studio.Resize(0f, -0.5f)),
                    new PickerButton("map", "MAP", Blue, studio.NextMap),
                    new PickerButton("lanehere", "LANE HERE", Blue, studio.LaneHere),
                    new PickerButton("actorhere", "ACTOR HERE", Blue, studio.ActorHere),
                    new PickerButton("save", "SAVE", Green, SaveSetup),
                    new PickerButton("cancel", "CANCEL", Red, studio.CancelSetup),
                };

            case StudioState.ChoosingSection:
                return new[]
                {
                    new PickerButton("fromearlier", "FROM -", Orange, () => studio.Nudge(start: true, -1)),
                    new PickerButton("fromlater", "FROM +", Orange, () => studio.Nudge(start: true, 1)),
                    new PickerButton("toearlier", "TO -", Orange, () => studio.Nudge(start: false, -1)),
                    new PickerButton("tolater", "TO +", Orange, () => studio.Nudge(start: false, 1)),
                    new PickerButton("preview", "PREVIEW", Blue, studio.PreviewSection),
                    new PickerButton("record", "RECORD", Red, () => studio.RecordSection()),
                    new PickerButton("mime", "MIME", Orange, () => studio.RecordSection(mime: true)),
                    new PickerButton("back", "BACK", Orange, studio.Back),
                };
        }

        var buttons = new List<PickerButton>
        {
            new("prev", "PREV", Orange, () => StepChart(-1)),
            new("start", "START", Green, StartBattle),
            new("next", "NEXT", Orange, () => StepChart(1)),
        };

        if (!Arena.InScene(ArenaKind.Gym)) buttons.Add(new PickerButton("gym", "GYM", Orange, Mod.Battle.ReturnToGym));

        ChartEntry? chosen = Chosen();
        if (_settings.Editing.Value && Studio.CanEdit(chosen))
        {
            buttons.Add(new PickerButton("setup", "SETUP", Blue, () => studio.BeginSetup(chosen!)));
            buttons.Add(new PickerButton("record", "RECORD", Red, () => RecordChosen()));
            buttons.Add(new PickerButton("mime", "MIME", Orange, () => RecordChosen(mime: true)));
            if (Studio.HasTrack(chosen)) buttons.Add(new PickerButton("rerecord", "RE-RECORD", Red, () => studio.ChooseSection(chosen!, _settings.ArenaOverride)));
            if (Studio.CanUndo(chosen)) buttons.Add(new PickerButton("undo", "UNDO", Orange, () => UndoChosen(chosen!)));
        }

        return buttons;
    }

    private PickerCard DescribePanel()
    {
        Studio studio = Mod.Studio;
        switch (studio.State)
        {
            case StudioState.Recording:
                (var seconds, var moves) = studio.Progress;
                var what = studio.IsSection ? $"{studio.Section.From:0.0}s to {studio.Section.To:0.0}s - stops by itself" : "STOP when done";
                return studio.Mime
                    ? new PickerCard($"<color=#ffa040><b>MIMING</b></color>\n<b>{studio.ActorName}</b>\n<size=60%>{seconds:0.0}s</size>\n<size=45%>throw the chart's shots by hand - nothing spawns. {what}</size>")
                    : new PickerCard($"<color=#ff5050><b>RECORDING</b></color>\n<b>{studio.ActorName}</b>\n<size=60%>{seconds:0.0}s  |  {moves} move{(moves == 1 ? "" : "s")}</size>\n<size=45%>{what}</size>");

            case StudioState.Deciding when studio.Take is { } take:
                var span = studio.IsSection ? $"{studio.Section.From:0.0}s to {studio.Section.To:0.0}s" : $"{take.Duration - take.Start:0.0}s";
                return new PickerCard(
                    $"<size=45%>{(studio.IsSection ? "SECTION" : "TAKE")}</size>\n<b>{take.Name}</b>\n<size=55%>{span}  |  {take.Moves.Count} move{(take.Moves.Count == 1 ? "" : "s")}</size>\n" +
                    $"<size=45%>keep it in {studio.Entry?.Name}, record again, watch it again, or throw it away</size>");

            case StudioState.Setup:
                (ArenaSpec arena, Lane lane, ActorSpec? actor) = studio.SetupValues;
                var where = arena.Map == ArenaKind.Here ? "here - wherever the player stands"
                    : arena.HasSpot ? $"{arena.Map}, from {arena.X:0.##}, {arena.Z:0.##} facing {arena.Facing:0}"
                    : $"{arena.Map}, laid across its middle";
                return new PickerCard(
                    $"<size=45%>LANE SETUP</size>\n<b>{studio.Entry?.Name}</b>\n<size=50%>{where}\n" +
                    $"{lane.Length:0.#} m long  |  {lane.Width:0.#} m wide{(actor == null ? "" : $"  |  actor at {actor.At.X:0.#}, {actor.At.Z:0.#}")}</size>\n" +
                    "<size=38%>walk the preview: LANE HERE starts the lane where you stand, facing the way you face; ACTOR HERE puts the actor where you stand</size>");

            case StudioState.ChoosingSection:
                return new PickerCard(
                    $"<size=45%>RE-RECORD</size>\n<b>{studio.ActorName}</b>\n<size=55%>{studio.Section.From:0.0}s to {studio.Section.To:0.0}s</size>\n" +
                    $"<size=45%><mspace=0.55em>{Timeline(studio.Section.From, studio.Section.To, studio.Chart?.End ?? studio.Section.To)}</mspace></size>\n" +
                    $"<size=38%>PREVIEW plays it; RECORD starts {Studio.PrerollSeconds:0} s before, where the actor stood</size>");
        }

        return DescribeChosen();
    }

    /// <summary>A section on a bar of the chart's length: <c>|-----#####--------|</c>.</summary>
    private static string Timeline(double from, double to, double end)
    {
        const int width = 28;
        var length = Math.Max(end, to);
        var bar = new char[width];
        for (var i = 0; i < width; i++)
        {
            var t = (i + 0.5) * length / width;
            bar[i] = t >= from && t < to ? '#' : '-';
        }

        return $"|{new string(bar)}|";
    }

    /// <summary>The chosen chart, where it is played and your best - or why it does not load.</summary>
    private PickerCard DescribeChosen()
    {
        var all = _library.Entries.ToList();
        ChartEntry? entry = Chosen();
        if (entry == null) return new PickerCard($"<b>No charts</b>\n<size=50%>New chart in the settings makes one in {_library.Folder}</size>");

        var position = $"<size=45%>CHART {all.IndexOf(entry) + 1} OF {all.Count}</size>\n";
        if (entry.Result.Chart is not { } chart)
        {
            var errors = string.Join("\n", entry.Result.Errors.Take(4)) + (entry.Result.Errors.Count > 4 ? $"\n...and {entry.Result.Errors.Count - 4} more (see the log)" : "");
            return new PickerCard(
                position + $"<b>{entry.Name}</b>\n<color=#ff7060><size=55%>does not load:</size></color>\n" +
                $"<size=38%>{errors}</size>\n<size=40%><i>fix and save {Path.GetFileName(entry.File)} - it reloads by itself</i></size>");
        }

        var arena = _settings.ArenaOverride is { } forced ? $"{forced} (override)" : chart.Arena.Map.ToString();
        var by = string.Join("  |  ", new[] { chart.Info.Author == null ? null : $"by {chart.Info.Author}", chart.Info.Difficulty }.Where(p => p != null));
        var facts = string.Join("  |  ", new[]
        {
            arena,
            $"{chart.End:0}s",
            $"{chart.Actors.Count} actor{(chart.Actors.Count == 1 ? "" : "s")}",
            chart.Music == null ? null : $"music: {Path.GetFileNameWithoutExtension(chart.Music.File)}",
            entry.Source == ChartSource.Package ? "package" : null,
        }.Where(p => p != null));
        BestScore? best = _scores.Best(entry.ScoreKey);

        return new PickerCard(
            position +
            $"<b>{chart.Name}</b>\n" +
            (by.Length > 0 ? $"<size=55%>{by}</size>\n" : "") +
            $"<size=50%>{facts}</size>\n" +
            (chart.Info.Description != null ? $"<size=45%><i>{chart.Info.Description}</i></size>\n" : "") +
            $"<size=55%>{(best == null ? "no best yet" : $"best: {best.Rank} {best.Score}")}</size>",
            chart.Cover == null ? null : ChartPaths.Inside(entry.AssetFolder, chart.Cover));
    }
}
