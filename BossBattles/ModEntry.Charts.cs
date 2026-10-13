using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using BossBattles.Actors;
using BossBattles.Battle;
using BossBattles.Services.Charts;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;
using UnityEngine;

namespace BossBattles;

/// <summary>Choosing, starting, making, recording into and sharing charts.</summary>
public sealed partial class ModEntry
{
    private long _chartsFingerprint;
    private string? _chosenFile;
    private float _nextChartsCheck;

    private void StartBattle()
    {
        ChartEntry? entry = Chosen();
        if (entry == null)
        {
            Logger.Warning($"[Battle.NoChart] no chart called \"{_settings.Chart.Value}\" in {_library.Folder}");
            return;
        }

        if (entry.Result.Chart == null)
        {
            Logger.Info($"{entry.Name} does not load:{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", entry.Result.Errors)}");
            return;
        }

        Mod.Battle.Start(entry.Result.Chart, _settings.ArenaOverride, entry.AssetFolder, entry.ScoreKey);
    }

    /// <summary>The chosen chart, even one that does not load (so the picker can say why), else the first playable one.</summary>
    private ChartEntry? Chosen()
    {
        // By file first: a chart that stops loading loses its name, and must stay chosen.
        ChartEntry? entry = (_chosenFile == null ? null : _library.Entries.FirstOrDefault(e => string.Equals(e.File, _chosenFile, StringComparison.OrdinalIgnoreCase)))
                            ?? _library.Find(_settings.Chart.Value)
                            ?? _library.Playable.FirstOrDefault()
                            ?? (_library.Entries.Count > 0 ? _library.Entries[0] : null);
        _chosenFile = entry?.File;
        return entry;
    }

    private void Choose(ChartEntry entry)
    {
        _chosenFile = entry.File;
        _settings.Chart.Value = entry.Name;
    }

    private void StepChart(int step)
    {
        var entries = _library.Entries.ToList();
        if (entries.Count == 0) return;

        var current = entries.IndexOf(Chosen()!);
        ChartEntry next = entries[(((current + step) % entries.Count) + entries.Count) % entries.Count];
        Choose(next);
        Logger.Info($"chart: {next.Name}");
    }

    private void ReloadCharts()
    {
        var written = _library.Reload();
        if (written > 0) Logger.Info($"wrote {written} starter chart(s) to {_library.Folder}");

        foreach (ChartEntry entry in _library.Entries.Where(e => !e.Result.Ok))
            Logger.Info($"chart {Path.GetFileName(entry.File)} has problems:{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", entry.Result.Errors)}");

        _settings.UpdateChartChoices(_library.Playable.Select(e => e.Name));
        foreach (ChartEntry entry in _library.Entries.Where(e => e.Source == ChartSource.Package && e.Result.Ok))
            Logger.Debug($"[Charts.Package] {Path.GetFileName(entry.File)}: {entry.Name}");
    }

    /// <summary>Reloads charts and clips when their files change, checked each second between battles.</summary>
    private void ReloadChartsWhenChanged()
    {
        if (Time.unscaledTime < _nextChartsCheck || Mod.Battle.State is not (BattleState.Idle or BattleState.Finished)) return;
        _nextChartsCheck = Time.unscaledTime + 1f;

        var fingerprint = FolderFingerprint.Of(_library.Folder);
        if (fingerprint == _chartsFingerprint) return;

        var first = _chartsFingerprint == 0;
        _chartsFingerprint = fingerprint;
        if (first) return;

        ReloadCharts();
        Logger.Info($"[Charts] reloaded: {_library.Playable.Count()} of {_library.Entries.Count} load");
    }

    private void IfEditing(Action action)
    {
        if (_settings.Editing.Value) action();
        else Logger.Info("turn on Chart editing in the BossBattles settings to use the chart-making tools");
    }

    private void RecordChosen(bool mime = false)
    {
        ChartEntry? entry = Chosen();
        if (!Studio.CanEdit(entry))
        {
            Logger.Info(entry?.Source == ChartSource.Package
                ? $"[Studio] {entry.Name} is a package - unzip it into a folder in the chart folder to record into it"
                : "[Studio] choose a chart that loads to record into");
            return;
        }

        Mod.Studio.Record(entry!, _settings.ArenaOverride, mime);
    }

    /// <summary>Runs a studio save, reloads the charts it changed, and says what happened.</summary>
    private void Saved(Func<string> save)
    {
        try
        {
            var what = save();
            ReloadCharts();
            _chartsFingerprint = FolderFingerprint.Of(_library.Folder);
            Logger.Info($"[Studio] {what}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            Logger.Info($"[Studio] could not save: {e.Message}");
        }
    }

    /// <summary>A new chart folder from the template, chosen; the boss wears your look and stones. Returns its folder.</summary>
    internal string? NewChart()
    {
        try
        {
            var name = Enumerable.Range(1, int.MaxValue).Select(n => n == 1 ? "My Chart" : $"My Chart {n}")
                .First(candidate => !_library.Entries.Any(e => string.Equals(e.Name, candidate, StringComparison.OrdinalIgnoreCase)));

            Player? you = PlayerManager.instance?.localPlayer;
            var look = you?.Data?.VisualData?.ToPlayfabDataString();
            List<string>? stones = you?.Controller == null ? null : Shiftstones.EquippedBy(you.Controller);
            var username = you?.Data?.GeneralData?.PublicUsername;
            var author = string.IsNullOrWhiteSpace(username) ? null : Regex.Replace(username, "<[^>]*>", "").Trim();
            var folder = ChartTemplate.Create(_library.Folder, name, author, look, stones);
            ReloadCharts();
            if (_library.Find(name) is { } made) Choose(made);
            else _settings.Chart.Value = name;
            _chartsFingerprint = FolderFingerprint.Of(_library.Folder);
            Logger.Info($"[Charts] new chart \"{name}\": edit {Path.Combine(folder, ChartLibrary.FolderChartFile)} - it reloads when you save");
            return folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Logger.Info($"[Charts] could not make a new chart: {e.Message}");
            return null;
        }
    }

    /// <summary>Opens the chosen chart's folder (else the Charts folder), to open in the desktop chart editor.</summary>
    private void OpenChartFolder()
    {
        try
        {
            ChartEntry? entry = Chosen();
            var folder = entry != null && Studio.CanEdit(entry) ? entry.AssetFolder : _library.Folder;
            Open(folder);
            Logger.Info($"[Charts] {entry?.Name ?? "the charts"}: {folder} - open it in the Boss Battles Chart Editor, save, and the game reloads");
        }
        catch (Exception e)
        {
            Logger.Info($"[Charts] could not open the chart folder: {e.Message}");
        }
    }

    private static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    private void ExportChosen()
    {
        ChartEntry? entry = Chosen();
        if (entry == null)
        {
            Logger.Info("[Export] no chart to export");
            return;
        }

        try
        {
            var output = Path.Combine(_folder, "Exports", ChartPackage.FileStem(entry.Name) + ChartPackage.Extension);
            PackResult packed = ChartPackage.Pack(entry, output);
            Logger.Info($"[Export] {entry.Name} -> {packed.Path} ({string.Join(", ", packed.Included)})" +
                    (packed.Missing.Count > 0 ? $" - missing: {string.Join(", ", packed.Missing)}" : ""));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Logger.Info($"[Export] {entry.Name} failed: {e.Message}");
        }
    }

    /// <summary>Prints your spot as a chart's "arena" line.</summary>
    private static void WhereAmI()
    {
        PlayerController? you = PlayerManager.instance?.localPlayer?.Controller;
        if (you == null) return;

        ArenaKind map = Arena.CurrentMap();
        var rig = ActorRig.From(you);
        Vector3 at = rig.Feet - (Arena.Anchor(map) ?? Vector3.zero);

        static string F(float v)
        {
            return v.ToString("0.0#", CultureInfo.InvariantCulture);
        }

        Logger.Info(map == ArenaKind.Here
            ? $"[Where] {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} is not a chart map (gym, ring, pit)"
            : $"[Where] \"arena\": {{ \"map\": \"{map.ToString().ToLowerInvariant()}\", \"at\": {{ \"x\": {F(at.x)}, \"z\": {F(at.z)} }}, \"facing\": {VectorHelpers.Bearing(rig.Facing).ToString("0", CultureInfo.InvariantCulture)} }}");
    }
}
