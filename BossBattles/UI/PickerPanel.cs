using BossBattles.Arenas;
using BossBattles.Battle;
using BossBattles.Editing;
using BossBattles.Services;
using BossBattles.Services.Charts;
using BossBattles.Services.Scoring;
using UnityEngine;

namespace BossBattles.UI;

// What the chart picker shows and where: the chosen chart between battles, and the studio's controls while recording.
internal class PickerPanel(ChartSelection charts, ChartLibrary library, JsonBook<BestScore> scores, Settings settings, BattleController battle, Studio studio)
{
    private static readonly Color Orange = new(1f, 0.45f, 0.1f);
    private static readonly Color Green = new(0.2f, 0.75f, 0.3f);
    private static readonly Color Red = new(0.85f, 0.2f, 0.2f);
    private static readonly Color Blue = new(0.25f, 0.5f, 0.95f);

    // Out of the way while you perform or walk the lane.
    public PickerPlacement Placement => studio.State is StudioState.Recording or StudioState.Setup ? PickerPlacement.Beside : PickerPlacement.InFront;

    public bool FollowsPlayer => studio.State == StudioState.Setup;

    public bool Wanted(bool pickerOpen) => studio.State switch
    {
        StudioState.Recording => battle.State == BattleState.Running,
        StudioState.Setup or StudioState.ChoosingSection => true,
        StudioState.Reviewing or StudioState.PreviewingSection => false,
        StudioState.Deciding => battle.BetweenBattles,
        _ => pickerOpen && !Maps.Online && battle.BetweenBattles,
    };

    public IReadOnlyList<PickerButton> Buttons()
    {
        switch (studio.State)
        {
            case StudioState.Recording:
                return [new PickerButton("stop", "STOP", Red, studio.StopRecording)];

            case StudioState.Deciding:
                return
                [
                    new PickerButton("keep", "KEEP", Green, () => charts.Saved(studio.KeepClip)),
                    new PickerButton("again", "AGAIN", Orange, studio.RestartRecording),
                    new PickerButton("replay", "REPLAY", Blue, studio.ReviewRecording),
                    new PickerButton("discard", "DISCARD", Red, studio.DiscardClip),
                ];

            case StudioState.Setup:
                return
                [
                    new PickerButton("longer", "LONGER", Orange, () => studio.LaneSetup.ResizeLane(1f, 0f)),
                    new PickerButton("shorter", "SHORTER", Orange, () => studio.LaneSetup.ResizeLane(-1f, 0f)),
                    new PickerButton("wider", "WIDER", Orange, () => studio.LaneSetup.ResizeLane(0f, 0.5f)),
                    new PickerButton("narrower", "NARROWER", Orange, () => studio.LaneSetup.ResizeLane(0f, -0.5f)),
                    new PickerButton("map", "MAP", Blue, studio.LaneSetup.SwitchToNextMap),
                    new PickerButton("lanehere", "LANE HERE", Blue, studio.LaneSetup.StartLaneWherePlayerStands),
                    new PickerButton("actorhere", "ACTOR HERE", Blue, studio.LaneSetup.MoveFirstActorWherePlayerStands),
                    new PickerButton("save", "SAVE", Green, () => charts.Saved(studio.SaveChartOptions)),
                    new PickerButton("cancel", "CANCEL", Red, studio.ExitChartOptions),
                ];

            case StudioState.ChoosingSection:
                return
                [
                    new PickerButton("fromearlier", "FROM -", Orange, () => studio.NudgeRecordingSection(start: true, -1)),
                    new PickerButton("fromlater", "FROM +", Orange, () => studio.NudgeRecordingSection(start: true, 1)),
                    new PickerButton("toearlier", "TO -", Orange, () => studio.NudgeRecordingSection(start: false, -1)),
                    new PickerButton("tolater", "TO +", Orange, () => studio.NudgeRecordingSection(start: false, 1)),
                    new PickerButton("preview", "PREVIEW", Blue, studio.PreviewSelectedSection),
                    new PickerButton("record", "RECORD", Red, () => studio.RecordSection()),
                    new PickerButton("mime", "MIME", Orange, () => studio.RecordSection(mime: true)),
                    new PickerButton("back", "BACK", Orange, studio.CloseSection),
                ];
        }

        var buttons = new List<PickerButton>
        {
            new("prev", "PREV", Orange, () => charts.StepChart(-1)),
            new("start", "START", Green, charts.StartBattle),
            new("next", "NEXT", Orange, () => charts.StepChart(1)),
        };

        if (!ArenaKind.Gym.IsLoaded()) buttons.Add(new PickerButton("gym", "GYM", Orange, battle.ReturnToGym));

        var chosen = charts.Chosen;
        if (!settings.Editing.Value || chosen is not { CanEdit: true }) return buttons;

        buttons.Add(new PickerButton("setup", "SETUP", Blue, () => studio.OpenChartOptions(chosen)));
        buttons.Add(new PickerButton("record", "RECORD", Red, () => charts.RecordChosen()));
        buttons.Add(new PickerButton("mime", "MIME", Orange, () => charts.RecordChosen(mime: true)));
        if (chosen.HasActorTrack) buttons.Add(new PickerButton("rerecord", "RE-RECORD", Red, () => studio.ChooseRecordingSection(chosen, settings.ArenaOverride)));
        if (Studio.CanUndo(chosen)) buttons.Add(new PickerButton("undo", "UNDO", Orange, () => charts.Saved(() => Studio.Undo(chosen))));
        return buttons;
    }

    public PickerCard Card()
    {
        switch (studio.State)
        {
            case StudioState.Recording:
                (var seconds, var moves) = studio.Progress;
                var what = studio.IsSectionOrWholeClip ? $"{studio.SelectedSection.From:0.0}s to {studio.SelectedSection.To:0.0}s - stops by itself" : "STOP when done";
                return studio.Mime
                    ? new PickerCard($"<color=#ffa040><b>MIMING</b></color>\n<b>{studio.ActorName}</b>\n<size=60%>{seconds:0.0}s</size>\n<size=45%>throw the chart's shots by hand - nothing spawns. {what}</size>")
                    : new PickerCard($"<color=#ff5050><b>RECORDING</b></color>\n<b>{studio.ActorName}</b>\n<size=60%>{seconds:0.0}s  |  {moves} move{(moves == 1 ? "" : "s")}</size>\n<size=45%>{what}</size>");

            case StudioState.Deciding when studio.NewClip is { } clip:
                var span = studio.IsSectionOrWholeClip ? $"{studio.SelectedSection.From:0.0}s to {studio.SelectedSection.To:0.0}s" : $"{clip.Duration - clip.Start:0.0}s";
                return new PickerCard(
                    $"<size=45%>{(studio.IsSectionOrWholeClip ? "SECTION" : "CLIP")}</size>\n<b>{clip.Name}</b>\n<size=55%>{span}  |  {clip.Moves.Count} move{(clip.Moves.Count == 1 ? "" : "s")}</size>\n" +
                    $"<size=45%>keep it in {studio.Entry?.Name}, record again, watch it again, or throw it away</size>");

            case StudioState.Setup:
                var arena = studio.LaneSetup.ArenaSpec;
                var lane = studio.LaneSetup.Lane;
                var actor = studio.LaneSetup.Actors.FirstOrDefault();
                var where = arena.Map == ArenaKind.Here ? "here - wherever the player stands"
                    : arena.HasSpot ? $"{arena.Map}, from {arena.X:0.##}, {arena.Z:0.##} facing {arena.Facing:0}"
                    : $"{arena.Map}, from where you stand";
                return new PickerCard(
                    $"<size=45%>LANE SETUP</size>\n<b>{studio.Entry?.Name}</b>\n<size=50%>{where}\n" +
                    $"{lane.Length:0.#} m long  |  {lane.Width:0.#} m wide{(actor == null ? "" : $"  |  actor at {actor.At.X:0.#}, {actor.At.Z:0.#}")}</size>\n" +
                    "<size=38%>walk the preview: LANE HERE starts the lane where you stand, facing the way you face; ACTOR HERE puts the actor where you stand</size>");

            case StudioState.ChoosingSection:
                // The section on a bar of the chart's length: |-----#####--------|
                var section = studio.SelectedSection;
                var chartLength = Math.Max(studio.Chart?.End ?? section.To, section.To);
                var timeline = string.Concat(Enumerable.Range(0, 28).Select(i => (i + 0.5) * chartLength / 28).Select(t => t >= section.From && t < section.To ? '#' : '-'));
                return new PickerCard(
                    $"<size=45%>RE-RECORD</size>\n<b>{studio.ActorName}</b>\n<size=55%>{section.From:0.0}s to {section.To:0.0}s</size>\n" +
                    $"<size=45%><mspace=0.55em>|{timeline}|</mspace></size>\n" +
                    $"<size=38%>PREVIEW plays it; RECORD starts {Studio.EaseInSeconds:0} s before, where the actor stood</size>");
        }

        return DescribeChosenChart();
    }

    // Where it is played and your best - or why it does not load.
    private PickerCard DescribeChosenChart()
    {
        var all = library.Entries.ToList();
        var entry = charts.Chosen;
        if (entry == null) return new PickerCard($"<b>No charts</b>\n<size=50%>New chart in the settings makes one in {library.Folder}</size>");

        var position = $"<size=45%>CHART {all.IndexOf(entry) + 1} OF {all.Count}</size>\n";
        if (entry.Result.Chart is not { } chart)
        {
            var errors = string.Join("\n", entry.Result.Errors.Take(4)) + (entry.Result.Errors.Count > 4 ? $"\n...and {entry.Result.Errors.Count - 4} more (see the log)" : "");
            return new PickerCard(
                position + $"<b>{entry.Name}</b>\n<color=#ff7060><size=55%>does not load:</size></color>\n" +
                $"<size=38%>{errors}</size>\n<size=40%><i>fix and save {Path.GetFileName(entry.File)} - it reloads by itself</i></size>");
        }

        var arena = settings.ArenaOverride is { } forced ? $"{forced} (override)" : chart.Arena.Map.ToString();
        var by = string.Join("  |  ", new[] { chart.Info.Author == null ? null : $"by {chart.Info.Author}", chart.Info.Difficulty }.Where(p => p != null));
        var facts = string.Join("  |  ", new[]
        {
            arena,
            $"{chart.End:0}s",
            $"{chart.Actors.Count} actor{(chart.Actors.Count == 1 ? "" : "s")}",
            chart.Music == null ? null : $"music: {Path.GetFileNameWithoutExtension(chart.Music.File)}",
            entry.Source == ChartSource.Package ? "package" : null,
        }.Where(p => p != null));
        var best = scores.Get(entry.ScoreKey);

        return new PickerCard(
            position +
            $"<b>{chart.Name}</b>\n" +
            (by.Length > 0 ? $"<size=55%>{by}</size>\n" : "") +
            $"<size=50%>{facts}</size>\n" +
            (chart.Info.Description != null ? $"<size=45%><i>{chart.Info.Description}</i></size>\n" : "") +
            $"<size=55%>{(best == null ? "no best yet" : $"best: {best.Rank} {best.Score}")}</size>",
            chart.Cover == null ? null : ChartFiles.Inside(entry.AssetFolder, chart.Cover));
    }
}
