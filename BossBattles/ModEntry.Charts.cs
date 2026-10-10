using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using BossBattles.Actors;
using BossBattles.Battle;
using BossBattles.Core;
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
            Mod.Note("Battle.NoChart", $"no chart called \"{_settings.Chart.Value}\" in {_library.Folder}");
            return;
        }

        if (entry.Result.Chart == null)
        {
            Mod.Say($"{entry.Name} does not load:{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", entry.Result.Errors)}");
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
        Mod.Say($"chart: {next.Name}");
    }

    private IEnumerable<string> PlayableNames() => _library.Playable.Select(e => e.Name);

    private void ReloadCharts()
    {
        Clips.Reload();
        foreach (var error in Clips.Errors) Mod.Say($"clip {error}");

        var written = _library.Reload();
        if (written > 0) Mod.Say($"wrote {written} starter chart(s) to {_library.Folder}");

        foreach (ChartEntry entry in _library.Entries.Where(e => !e.Result.Ok))
            Mod.Say($"chart {Path.GetFileName(entry.File)} has problems:{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", entry.Result.Errors)}");

        _settings.UpdateChartChoices(PlayableNames());
        foreach (ChartEntry entry in _library.Entries.Where(e => e.Source == ChartSource.Package && e.Result.Ok))
            Mod.Note("Charts.Package", $"{Path.GetFileName(entry.File)}: {entry.Name}", echo: false);
    }

    /// <summary>Reloads charts and clips when their files change, checked each second between battles.</summary>
    private void ReloadChartsWhenChanged()
    {
        if (Time.unscaledTime < _nextChartsCheck || Mod.Battle.State is not (BattleState.Idle or BattleState.Finished)) return;
        _nextChartsCheck = Time.unscaledTime + 1f;

        var fingerprint = FolderFingerprint.Of(_library.Folder, Clips.Folder);
        if (fingerprint == _chartsFingerprint) return;

        var first = _chartsFingerprint == 0;
        _chartsFingerprint = fingerprint;
        if (first) return;

        ReloadCharts();
        Mod.Say($"[Charts] reloaded: {_library.Playable.Count()} of {_library.Entries.Count} load");
    }

    private void IfEditing(Action action)
    {
        if (_settings.Editing.Value) action();
        else Mod.Say("turn on Chart editing in the BossBattles settings to use the chart-making tools");
    }

    private void RecordChosen(bool mime = false)
    {
        ChartEntry? entry = Chosen();
        if (!Studio.CanEdit(entry))
        {
            Mod.Say(entry?.Source == ChartSource.Package
                ? $"[Studio] {entry.Name} is a package - unzip it into a folder in the chart folder to record into it"
                : "[Studio] choose a chart that loads to record into");
            return;
        }

        Mod.Studio.Record(entry!, _settings.ArenaOverride, mime);
    }

    private void SaveSetup() => Saved(Mod.Studio.SaveSetup);

    private void UndoChosen(ChartEntry entry) => Saved(() => Studio.Undo(entry));

    private void KeepTake() => Saved(Mod.Studio.Keep);

    /// <summary>Runs a studio save, reloads the charts it changed, and says what happened.</summary>
    private void Saved(Func<string> save)
    {
        try
        {
            var what = save();
            ReloadCharts();
            _chartsFingerprint = FolderFingerprint.Of(_library.Folder, Clips.Folder);
            Mod.Say($"[Studio] {what}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            Mod.Say($"[Studio] could not save: {e.Message}");
        }
    }

    /// <summary>A new chart folder from the template, chosen; the boss wears your look and stones. Returns its folder.</summary>
    internal string? NewChart()
    {
        try
        {
            var name = "My Chart";
            for (var n = 2; _library.Entries.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)); n++) name = $"My Chart {n}";

            Player? you = PlayerManager.instance?.localPlayer;
            var look = you?.Data?.VisualData?.ToPlayfabDataString();
            List<string>? stones = you?.Controller == null ? null : Shiftstones.EquippedBy(you.Controller);
            var folder = ChartTemplate.Create(_library.Folder, name, PlayerName(), look, stones);
            ReloadCharts();
            if (_library.Find(name) is { } made) Choose(made);
            else _settings.Chart.Value = name;
            _chartsFingerprint = FolderFingerprint.Of(_library.Folder, Clips.Folder);
            Mod.Say($"[Charts] new chart \"{name}\": edit {Path.Combine(folder, ChartLibrary.FolderChartFile)} - it reloads when you save");
            return folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Mod.Say($"[Charts] could not make a new chart: {e.Message}");
            return null;
        }
    }

    /// <summary>Your in-game name without its rich-text tags.</summary>
    private static string? PlayerName()
    {
        try
        {
            var name = PlayerManager.instance?.localPlayer?.Data?.GeneralData?.PublicUsername;
            return string.IsNullOrWhiteSpace(name) ? null : Regex.Replace(name, "<[^>]*>", "").Trim();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Writes the bundled chart editor to UserData and opens it, with the chosen chart's folder beside it.</summary>
    private void OpenEditor()
    {
        try
        {
            using Stream? resource = typeof(ModEntry).Assembly.GetManifestResourceStream("BossBattles.ChartEditor.html");
            if (resource == null)
            {
                Mod.Say("[Editor] this build has no chart editor inside it");
                return;
            }

            var page = Path.Combine(_folder, "Editor", "index.html");
            Directory.CreateDirectory(Path.GetDirectoryName(page)!);
            using (FileStream file = File.Create(page)) resource.CopyTo(file);

            Open(page);
            ChartEntry? entry = Chosen();
            var folder = entry != null && Studio.CanEdit(entry) ? entry.AssetFolder : _library.Folder;
            Open(folder);
            Mod.Say($"[Editor] opened the chart editor; {entry?.Name ?? "the charts"} are in {folder} - open them in it, save, and the game reloads");
        }
        catch (Exception e)
        {
            Mod.Say($"[Editor] could not open the chart editor: {e.Message}");
        }
    }

    private static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    private void ExportChosen()
    {
        ChartEntry? entry = Chosen();
        if (entry == null)
        {
            Mod.Say("[Export] no chart to export");
            return;
        }

        try
        {
            var output = Path.Combine(_folder, "Exports", ChartPackage.FileStem(entry.Name) + ChartPackage.Extension);
            PackResult packed = ChartPackage.Pack(entry, output, Clips.Find);
            Mod.Say($"[Export] {entry.Name} -> {packed.Path} ({string.Join(", ", packed.Included)})" +
                    (packed.Missing.Count > 0 ? $" - missing: {string.Join(", ", packed.Missing)}" : ""));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Mod.Say($"[Export] {entry.Name} failed: {e.Message}");
        }
    }

    /// <summary>Prints your spot as a chart's "arena" line.</summary>
    private static void WhereAmI()
    {
        PlayerController? you = PlayerManager.instance?.localPlayer?.Controller;
        if (you == null) return;

        ArenaKind map = Arena.CurrentMap();
        var rig = ActorRig.Of(you);
        Vector3 at = rig.FloorPoint - (Arena.Anchor(map) ?? Vector3.zero);

        static string F(float v)
        {
            return v.ToString("0.0#", CultureInfo.InvariantCulture);
        }

        Mod.Say(map == ArenaKind.Here
            ? $"[Where] {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} is not a chart map (gym, ring, pit)"
            : $"[Where] \"arena\": {{ \"map\": \"{map.ToString().ToLowerInvariant()}\", \"at\": {{ \"x\": {F(at.x)}, \"z\": {F(at.z)} }}, \"facing\": {Arena.Bearing(rig.Facing).ToString("0", CultureInfo.InvariantCulture)} }}");
    }
}
