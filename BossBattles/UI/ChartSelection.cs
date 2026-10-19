using System.Diagnostics;
using System.Text.RegularExpressions;
using BossBattles.Actors;
using BossBattles.Battle;
using BossBattles.Editing;
using BossBattles.Services.Charts;
using UnityEngine;

namespace BossBattles.UI;

// The chart you have chosen, and everything done to it: starting, recording into, making, opening and exporting.
internal class ChartSelection(ChartLibrary library, Settings settings, BattleController battle, Studio studio, string exportsFolder)
{
    private long _chartsFingerprint;
    private string? _chosenFile;
    private float _nextChartsCheck;

    // Even one that does not load (so the picker can say why), else the first playable one.
    public ChartEntry? Chosen
    {
        get
        {
            // By file first: a chart that stops loading loses its name, and must stay chosen.
            var entry = (_chosenFile == null ? null : library.Entries.FirstOrDefault(e => string.Equals(e.File, _chosenFile, StringComparison.OrdinalIgnoreCase)))
                        ?? library.Find(settings.Chart.Value)
                        ?? library.Playable.FirstOrDefault()
                        ?? (library.Entries.Count > 0 ? library.Entries[0] : null);
            _chosenFile = entry?.File;
            return entry;
        }
    }

    public void StepChart(int step)
    {
        var entries = library.Entries.ToList();
        if (entries.Count == 0) return;

        var current = entries.IndexOf(Chosen!);
        var next = entries[(((current + step) % entries.Count) + entries.Count) % entries.Count];
        Choose(next);
        Logger.Info($"chart: {next.Name}");
    }

    public void StartBattle()
    {
        var entry = Chosen;
        if (entry == null)
        {
            Logger.Warning($"[Battle.NoChart] no chart called \"{settings.Chart.Value}\" in {library.Folder}");
            return;
        }

        if (entry.Result.Chart == null)
        {
            Logger.Info($"{entry.Name} does not load:{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", entry.Result.Errors)}");
            return;
        }

        battle.Start(new BattleRequest(entry.Result.Chart) { ArenaOverride = settings.ArenaOverride, AssetFolder = entry.AssetFolder, ScoreKey = entry.ScoreKey });
    }

    public void RecordChosen(bool mime = false)
    {
        var entry = Chosen;
        if (entry is not { CanEdit: true })
        {
            Logger.Info(entry?.Source == ChartSource.Package
                ? $"[Studio] {entry.Name} is a package - unzip it into a folder in the chart folder to record into it"
                : "[Studio] choose a chart that loads to record into");
            return;
        }

        studio.StartRecording(entry, settings.ArenaOverride, mime);
    }

    public void ReloadCharts()
    {
        var written = library.Reload();
        if (written > 0) Logger.Info($"wrote {written} starter chart(s) to {library.Folder}");

        foreach (var entry in library.Entries.Where(e => !e.Result.Ok))
            Logger.Info($"chart {Path.GetFileName(entry.File)} has problems:{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", entry.Result.Errors)}");

        settings.UpdateChartChoices(library.Playable.Select(e => e.Name));
        foreach (var entry in library.Entries.Where(e => e.Source == ChartSource.Package && e.Result.Ok))
            Logger.Debug($"[Charts.Package] {Path.GetFileName(entry.File)}: {entry.Name}");
    }

    // Checked each second between battles.
    public void ReloadChartsWhenChanged()
    {
        if (Time.unscaledTime < _nextChartsCheck || !battle.BetweenBattles) return;
        _nextChartsCheck = Time.unscaledTime + 1f;

        var fingerprint = ChartFiles.Fingerprint(library.Folder);
        if (fingerprint == _chartsFingerprint) return;

        var first = _chartsFingerprint == 0;
        _chartsFingerprint = fingerprint;
        if (first) return;

        ReloadCharts();
        Logger.Info($"[Charts] reloaded: {library.Playable.Count()} of {library.Entries.Count} load");
    }

    // Runs a studio save, reloads the charts it changed, and says what happened.
    public void Saved(Func<string> save)
    {
        try
        {
            var what = save();
            ReloadCharts();
            _chartsFingerprint = ChartFiles.Fingerprint(library.Folder);
            Logger.Info($"[Studio] {what}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            Logger.Info($"[Studio] could not save: {e.Message}");
        }
    }

    // A new chart folder from the template, chosen; the boss wears your look and stones.
    public string? NewChart()
    {
        try
        {
            var name = ChartFiles.FirstFreeName("My Chart", " ", candidate => library.Entries.Any(e => string.Equals(e.Name, candidate, StringComparison.OrdinalIgnoreCase)));

            var you = BattlePlayer.Player;
            var look = you?.Data?.VisualData?.ToPlayfabDataString();
            var stones = you?.Controller == null ? null : Shiftstones.EquippedBy(you.Controller);
            var username = you?.Data?.GeneralData?.PublicUsername;
            var author = string.IsNullOrWhiteSpace(username) ? null : Regex.Replace(username, "<[^>]*>", "").Trim();
            var folder = ChartTemplate.Create(library.Folder, name, author, look, stones);
            ReloadCharts();
            if (library.Find(name) is { } made) Choose(made);
            else settings.Chart.Value = name;
            _chartsFingerprint = ChartFiles.Fingerprint(library.Folder);
            Logger.Info($"[Charts] new chart \"{name}\": edit {Path.Combine(folder, ChartLibrary.FolderChartFile)} - it reloads when you save");
            return folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Logger.Info($"[Charts] could not make a new chart: {e.Message}");
            return null;
        }
    }

    // The chosen chart's folder (else the Charts folder), to open in the desktop chart editor.
    public void OpenChartFolder()
    {
        try
        {
            var entry = Chosen;
            var folder = entry is { CanEdit: true } ? entry.AssetFolder : library.Folder;
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })?.Dispose();
            Logger.Info($"[Charts] {entry?.Name ?? "the charts"}: {folder} - open it in the Boss Battles Chart Editor, save, and the game reloads");
        }
        catch (Exception e)
        {
            Logger.Info($"[Charts] could not open the chart folder: {e.Message}");
        }
    }

    public void ExportChosen()
    {
        var entry = Chosen;
        if (entry == null)
        {
            Logger.Info("[Export] no chart to export");
            return;
        }

        try
        {
            var packed = ChartPackage.Pack(entry, Path.Combine(exportsFolder, ChartFiles.FileStem(entry.Name) + ChartPackage.Extension));
            Logger.Info($"[Export] {entry.Name} -> {packed.Path} ({string.Join(", ", packed.Included)})" +
                        (packed.Missing.Count > 0 ? $" - missing: {string.Join(", ", packed.Missing)}" : ""));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Logger.Info($"[Export] {entry.Name} failed: {e.Message}");
        }
    }

    private void Choose(ChartEntry entry)
    {
        _chosenFile = entry.File;
        settings.Chart.Value = entry.Name;
    }
}
