using Basalt.Arenas;
using Basalt.Battle;
using Basalt.Editing;
using Basalt.Services;
using Basalt.Services.Charts;
using Basalt.Services.Scoring;
using static Basalt.Menu.MenuColours;

namespace Basalt.Menu.Screens;

// Choosing a song and its difficulty, starting it, and the chart-making tools when editing is on.
internal class ChartsScreen(ChartSelection charts, ChartLibrary library, JsonBook<BestScore> scores, Settings settings, BattleController battle, Studio studio) : IMenuScreen
{
    public IReadOnlyList<MenuButton> Buttons
    {
        get
        {
            var buttons = new List<MenuButton>
            {
                new("prev", "PREV", Orange, () => charts.StepSong(-1)),
                new("start", "START", Green, charts.StartBattle),
                new("next", "NEXT", Orange, () => charts.StepSong(1)),
            };

            if (!ArenaKind.Gym.IsLoaded()) buttons.Add(new MenuButton("gym", "GYM", Orange, battle.ReturnToGym));

            if (settings.Editing.Value && charts.Chosen is { CanEdit: true } chosen)
            {
                buttons.Add(new MenuButton("setup", "SETUP", Blue, () => studio.OpenChartOptions(chosen)));
                buttons.Add(new MenuButton("record", "RECORD", Red, () => charts.RecordChosen()));
                buttons.Add(new MenuButton("mime", "MIME", Orange, () => charts.RecordChosen(mime: true)));

                if (Studio.CanUndo(chosen)) buttons.Add(new MenuButton("undo", "UNDO", Orange, () => charts.Saved(() => Studio.Undo(chosen))));
            }

            return buttons;
        }
    }

    public MenuSlider? Slider
    {
        get
        {
            if (charts.Chosen is not { } chosen) return null;

            var difficulties = library.DifficultiesOf(chosen);
            if (difficulties.Count < 2) return null;

            var sharedDifficulties = difficulties.GroupBy(e => e.Difficulty, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var labels = difficulties.Select(entry => (entry.Difficulty == null || sharedDifficulties.Contains(entry.Difficulty) ? entry.Stem : entry.Difficulty).ToUpperInvariant()).ToList();
            return new MenuSlider($"difficulty:{chosen.Name}", labels, difficulties.ToList().IndexOf(chosen), step => charts.ChooseDifficulty(difficulties[step]));
        }
    }

    public MenuCard Card
    {
        get
        {
            var songs = library.Songs.ToList();
            var entry = charts.Chosen;
            if (entry == null) return new MenuCard($"No charts");

            var song = songs.FindIndex(s => string.Equals(s.Name, entry.Name, StringComparison.OrdinalIgnoreCase));

            var position = $"<size=45%>Song {song + 1} of {songs.Count}</size>\n";

            if (entry.Result.Chart is not { } chart)
            {
                var errors = string.Join("\n", entry.Result.Errors.Take(4)) + (entry.Result.Errors.Count > 4 ? $"\n...and {entry.Result.Errors.Count - 4} more (see the log)" : "");
                return new MenuCard(
                    position + $"{entry.Name}\n<color=#ff7060><size=55%>does not load:</size></color>\n" +
                    $"<size=38%>{errors}</size>\n<size=40%><i>fix and save {Path.GetFileName(entry.File)}</i></size>");
            }

            var by = string.Join("  |  ", new[] { chart.Info.Author == null ? null : $"by {chart.Info.Author}", chart.Info.Difficulty }.Where(p => p != null));
            var facts = string.Join("  |  ", new[]
            {
                chart.Arena.Map.ToString(),
                $"{chart.End:0}s",
                chart.Music == null ? null : $"music: {Path.GetFileNameWithoutExtension(chart.Music.File)}"
            }.Where(p => p != null));

            var best = scores.Get(entry.ScoreKey);

            return new MenuCard(
                position +
                $"{chart.Name}\n" +
                (by.Length > 0 ? $"<size=55%>{by}</size>\n" : "") +
                $"<size=50%>{facts}</size>\n" +
                (chart.Info.Description != null ? $"<size=45%><i>{chart.Info.Description}</i></size>\n" : "") +
                $"<size=55%>{(best == null ? "no best yet" : $"best: {best.Rank} {best.Score}")}</size>",
                chart.Cover == null ? null : ChartFiles.Inside(entry.AssetFolder, chart.Cover));
        }
    }
}
