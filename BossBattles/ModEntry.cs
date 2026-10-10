using System.Globalization;
using BossBattles.Actors;
using BossBattles.Battle;
using BossBattles.Core;
using BossBattles.Flatscreen;
using BossBattles.Patches;
using BossBattles.Services.Charts;
using BossBattles.Services.Clips;
using BossBattles.Services.Logging;
using BossBattles.Services.Placement;
using BossBattles.Services.Scoring;
using BossBattles.Testing;
using BossBattles.UI;
using Il2CppRUMBLE.Managers;
using Il2CppRUMBLE.Players;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

[assembly: MelonInfo(typeof(BossBattles.ModEntry), "BossBattles", "1.0.0", "ACutiePi")]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]
[assembly: MelonOptionalDependencies("UIFramework")]

// MelonLoader would otherwise PatchAll this assembly before OnInitializeMelon, then each group
// would be patched a second time when applied deliberately.
[assembly: HarmonyDontPatchAll]

namespace BossBattles;

// Named ModEntry rather than Mod because CA1716 flags `Mod` as a reserved language keyword.
public sealed class ModEntry : MelonMod
{
    private const string Keys =
        "Ctrl+B start/stop the battle | Ctrl+N / Ctrl+Shift+N next / previous chart | Ctrl+M chart picker | Ctrl+A arena (chart/here/Gym/Ring/Pit) | " +
        "Ctrl+R reload charts (automatic when they change) | Ctrl+G back to the Gym | " +
        "with Chart editing on: Ctrl+Shift+E chart editor | Ctrl+Shift+B record the actor | Ctrl+Shift+C new chart | Ctrl+E export the chart as a .bbchart | Ctrl+W where am I (for arena.at) | " +
        "Ctrl+F desktop camera | Ctrl+Shift+R record a clip | Ctrl+T test actor (Ctrl+Shift+T puppet) | Ctrl+9 test actor mirror/bot | Ctrl+P plant | " +
        "Ctrl+Shift+P test actor plays the newest clip | Ctrl+K knockback | Ctrl+0 census. " +
        "No headset: numpad 8/2/4/6 walk, 7/9 turn.";

    private readonly Settings _settings = new();
    private ChartLibrary _library = null!;
    private ClipLibrary _clips = null!;
    private readonly ClipRecorder _recorder = new();
    private string _folder = "";
    private ScoreBook _scores = null!;
    private ChartPicker _picker = null!;
    private long _chartsFingerprint;
    private string? _chosenFile;
    private float _nextChartsCheck;

    // Orbits the battle's first actor, the test actor, or you. Self-contained (Flatscreen/).
    private readonly DesktopCamera _desktopCamera = new(
        () => Mod.Actors.First?.HeadPosition ?? Camera.main?.transform.position,
        message => Mod.Note("DesktopCamera", message));

    public override void OnInitializeMelon()
    {
        var folder = Path.Combine(MelonEnvironment.UserDataDirectory, "BossBattles");
        _folder = folder;
        Mod.Melon = LoggerInstance;
        Mod.Log = new ModLog(Path.Combine(folder, "battles.log"));

        _clips = new ClipLibrary(Path.Combine(folder, "Clips"));
        Mod.Battle.Clips = _clips;
        _library = new ChartLibrary(Path.Combine(folder, "Charts"));
        _scores = new ScoreBook(Path.Combine(folder, "scores.json"));
        _scores.Load();
        if (_scores.LoadError != null) Mod.Say($"scores.json could not be read ({_scores.LoadError}) - starting a fresh book");
        Mod.Battle.Scores = _scores;
        var places = new SpotBook(Path.Combine(folder, "places.json"));
        places.Load();
        _picker = new ChartPicker(DescribePanel, PanelButtons, PanelWanted, () => _desktopCamera.Camera, places);
        ReloadCharts();

        _settings.Create(PlayableNames());
        ApplySettings();
        _settings.Countdown.OnEntryValueChanged.Subscribe((_, _) => ApplySettings());
        _settings.ActorKnockbackTaken.OnEntryValueChanged.Subscribe((_, _) => ApplySettings());
        _settings.ShowBackstop.OnEntryValueChanged.Subscribe((_, _) => ApplySettings());
        _settings.MusicVolume.OnEntryValueChanged.Subscribe((_, _) => ApplySettings());
        _settings.ChartVisuals.OnEntryValueChanged.Subscribe((_, _) => ApplySettings());
        _settings.ShowPhaseBanners.OnEntryValueChanged.Subscribe((_, _) => ApplySettings());
        _settings.ShowPicker.OnEntryValueChanged.Subscribe((_, _) => ApplySettings());
        _settings.PickerDistance.OnEntryValueChanged.Subscribe((_, _) => ApplySettings());
        _settings.PickerHeight.OnEntryValueChanged.Subscribe((_, _) => ApplySettings());
        _settings.PickerSide.OnEntryValueChanged.Subscribe((_, _) => ApplySettings());
        _settings.PickerFixedInGym.OnEntryValueChanged.Subscribe((_, _) => ApplySettings());

        Mod.Note("Init", $"actor patches: {ActorPatches.Apply(HarmonyInstance)} methods", echo: false);
        Mod.Note("Init", $"arena patches: {ArenaPatches.Apply(HarmonyInstance)} methods", echo: false);
        FlatscreenControls.Log = message => Mod.Note("Flatscreen", message);
        Mod.Note("Init", $"flatscreen controls: {FlatscreenControls.Apply(HarmonyInstance)} methods", echo: false);

        UiBridge.Register(this, _settings.Category, new (string, string, string, Action)[]
        {
            ("Start", "Start battle", "Play the chosen chart in the chosen arena.", StartBattle),
            ("Stop", "Stop battle", "End the battle and remove its actors.", () => Mod.Battle.Stop("stopped from the menu")),
            ("Reload", "Reload charts", "Read UserData/BossBattles/Charts again - for editing charts while the game runs.", ReloadCharts),
            ("Picker", "Chart picker here", "Bring the chart picker in front of you - in the Gym, this sets its fixed spot.", _picker.Summon),
            ("New", "New chart", "Chart editing: make a new chart folder from a commented template, ready to play and edit - and pick it.", () => IfEditing(() => NewChart())),
            ("Editor", "Open chart editor", "Chart editing: open the chart editor in your browser - timeline, waveform, lane view. Open your chart's folder in it.", () => IfEditing(OpenEditor)),
            ("Record", "Record actor", "Chart editing: record the chosen chart's actor - you stand in its place and perform.", () => IfEditing(() => RecordChosen())),
            ("Export", "Export chart", "Chart editing: pack the chosen chart, its song and clips into UserData/BossBattles/Exports/<name>.bbchart for sharing.", () => IfEditing(ExportChosen)),
            ("Gym", "Back to the Gym", "Leave the Ring or Pit.", Mod.Battle.ReturnToGym),
        });

        SelfTest.Configure(_clips, _desktopCamera.ShowFixed, _desktopCamera.CapturePng, _picker, NewChart);

        Mod.Say($"BossBattles loaded: {_library.Playable.Count()} chart(s). {Keys}");
    }

    // A throw escaping a MelonLoader callback repeats every frame; catch at the boundary.
    public override void OnUpdate()
    {
        try
        {
            HandleKeys();
            ReloadChartsWhenChanged();
            TestBench.Update();
            _recorder.Update();
            FlatscreenControls.Update();
            Mod.Actors.Update();
            Mod.Battle.Update();
            Mod.Studio.Update();
            _picker.Placement = Mod.Studio.State is StudioState.Recording or StudioState.Setup ? PickerPlacement.Beside : PickerPlacement.InFront;
            _picker.Follow = Mod.Studio.State == StudioState.Setup;
            _picker.Update();
            SelfTest.Update();
        }
        catch (Exception e)
        {
            Mod.Note("UpdateFailed", e.ToString());
        }
    }

    // After the game's own LateUpdates, so floating UI and the camera follow final poses.
    public override void OnLateUpdate()
    {
        try
        {
            Mod.Actors.LateUpdate();
            ActorSoundScope.Reset();
            _desktopCamera.LateUpdate();
        }
        catch (Exception e)
        {
            Mod.Note("LateUpdateFailed", e.ToString());
        }
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        Mod.Actors.ForgetSceneObjects();
        TestBench.Forget();
        Mod.Battle.OnSceneLoaded(sceneName);
        Mod.Studio.OnSceneLoaded();
        _picker.Forget();
        _desktopCamera.Forget();
        SelfTest.OnSceneLoaded(sceneName);
        Mod.Report($"entered {sceneName}");
    }

    public override void OnApplicationQuit()
    {
        Mod.Battle.RestoreVisuals();
        Mod.Report("quit");
        Mod.Log?.Dispose();
    }

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

    /// <summary>The chosen chart - which may be one that does not load, so the picker can say why - or the first playable one.</summary>
    private ChartEntry? Chosen()
    {
        // By its file first: a chart that stops loading loses its name, and must stay chosen (showing
        // why) rather than another chart silently taking its place.
        ChartEntry? entry = (_chosenFile == null ? null : _library.Entries.FirstOrDefault(e => string.Equals(e.File, _chosenFile, StringComparison.OrdinalIgnoreCase)))
                            ?? _library.Find(_settings.Chart.Value)
                            ?? _library.Playable.FirstOrDefault()
                            ?? (_library.Entries.Count > 0 ? _library.Entries[0] : null);
        _chosenFile = entry?.File;
        return entry;
    }

    /// <summary>Choose a chart by name, remembering its file.</summary>
    private void Choose(ChartEntry entry)
    {
        _chosenFile = entry.File;
        _settings.Chart.Value = entry.Name;
    }

    private static readonly Color Orange = new(1f, 0.45f, 0.1f);
    private static readonly Color Green = new(0.2f, 0.75f, 0.3f);
    private static readonly Color Red = new(0.85f, 0.2f, 0.2f);
    private static readonly Color Blue = new(0.25f, 0.5f, 0.95f);

    /// <summary>The panel: between battles (if open); beside you while recording; through setup and section choosing; on a take's review.</summary>
    private bool PanelWanted() => Mod.Studio.State switch
    {
        StudioState.Recording => Mod.Battle.State == BattleState.Running,
        StudioState.Setup or StudioState.ChoosingSection => true,
        StudioState.Reviewing or StudioState.PreviewingSection => false,
        StudioState.Deciding => Mod.Battle.State is BattleState.Idle or BattleState.Finished,
        _ => _picker.Open && Mod.Battle.State is BattleState.Idle or BattleState.Finished,
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
                var what = studio.IsSection ? $"{studio.Section.From:0.0}s to {studio.Section.To:0.0}s - stops by itself" : "STOP (or Ctrl+B) when done";
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

    /// <summary>Runs a studio save, reloads the charts it changed, and says what happened.</summary>
    private void Saved(Func<string> save)
    {
        try
        {
            var what = save();
            ReloadCharts();
            _chartsFingerprint = FolderFingerprint.Of(_library.Folder, _clips.Folder);
            Mod.Say($"[Studio] {what}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            Mod.Say($"[Studio] could not save: {e.Message}");
        }
    }

    private void KeepTake() => Saved(Mod.Studio.Keep);

    /// <summary>
    /// Writes the chart editor (inside the mod) to UserData/BossBattles/Editor and opens it in your
    /// browser, and opens the chosen chart's folder beside it - drag its files into the editor, or
    /// use the editor's Open folder (Chrome, Edge) to save straight back.
    /// </summary>
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

    private static void Open(string path) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    private void IfEditing(Action action)
    {
        if (_settings.Editing.Value) action();
        else Mod.Say("turn on Chart editing in the BossBattles settings to use the chart-making tools");
    }

    /// <summary>The picker's card: the chosen chart, what it is, where it is played, and your best - or why it does not load.</summary>
    private PickerCard DescribeChosen()
    {
        var all = _library.Entries.ToList();
        ChartEntry? entry = Chosen();
        if (entry == null) return new PickerCard($"<b>No charts</b>\n<size=50%>Ctrl+Shift+C (or New chart) makes one in {_library.Folder}</size>");

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

    /// <summary>A new chart folder from the template, named so it is easy to find, and chosen; returns its folder.</summary>
    private string? NewChart()
    {
        try
        {
            var name = "My Chart";
            for (var n = 2; _library.Entries.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)); n++) name = $"My Chart {n}";

            // The boss wears your look and stones as you make it - what everyone you share it with fights.
            Player? you = PlayerManager.instance?.localPlayer;
            var look = you?.Data?.VisualData?.ToPlayfabDataString();
            List<string>? stones = you?.Controller == null ? null : Shiftstones.EquippedBy(you.Controller);
            var folder = ChartTemplate.Create(_library.Folder, name, PlayerName(), look, stones);
            ReloadCharts();
            if (_library.Find(name) is { } made) Choose(made);
            else _settings.Chart.Value = name;
            _chartsFingerprint = FolderFingerprint.Of(_library.Folder, _clips.Folder);
            Mod.Say($"[Charts] new chart \"{name}\": edit {Path.Combine(folder, ChartLibrary.FolderChartFile)} - it reloads when you save");
            return folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Mod.Say($"[Charts] could not make a new chart: {e.Message}");
            return null;
        }
    }

    /// <summary>Your in-game name without its formatting, for a new chart's author.</summary>
    private static string? PlayerName()
    {
        try
        {
            var name = PlayerManager.instance?.localPlayer?.Data?.GeneralData?.PublicUsername;
            return string.IsNullOrWhiteSpace(name) ? null : System.Text.RegularExpressions.Regex.Replace(name, "<[^>]*>", "").Trim();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Charts (and clips) reload by themselves when their files change - between battles, checked each second.</summary>
    private void ReloadChartsWhenChanged()
    {
        if (Time.unscaledTime < _nextChartsCheck || Mod.Battle.State is not (BattleState.Idle or BattleState.Finished)) return;
        _nextChartsCheck = Time.unscaledTime + 1f;

        var fingerprint = FolderFingerprint.Of(_library.Folder, _clips.Folder);
        if (fingerprint == _chartsFingerprint) return;

        var first = _chartsFingerprint == 0;
        _chartsFingerprint = fingerprint;
        if (first) return;

        ReloadCharts();
        Mod.Say($"[Charts] reloaded: {_library.Playable.Count()} of {_library.Entries.Count} load");
    }

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
            PackResult packed = ChartPackage.Pack(entry, output, _clips.Find);
            Mod.Say($"[Export] {entry.Name} -> {packed.Path} ({string.Join(", ", packed.Included)})" +
                    (packed.Missing.Count > 0 ? $" - missing: {string.Join(", ", packed.Missing)}" : ""));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Mod.Say($"[Export] {entry.Name} failed: {e.Message}");
        }
    }

    /// <summary>Prints your spot as a chart's "arena" line, measured the way charts measure it.</summary>
    private static void WhereAmI()
    {
        PlayerController? you = PlayerManager.instance?.localPlayer?.Controller;
        if (you == null) return;

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        ArenaKind map = new[] { ArenaKind.Gym, ArenaKind.Ring, ArenaKind.Pit }.FirstOrDefault(k => Arena.SceneFor(k) == scene);
        var rig = ActorRig.Of(you);
        Vector3 at = rig.FloorPoint - (Arena.Anchor(map) ?? Vector3.zero);

        static string F(float v)
        {
            return v.ToString("0.0#", CultureInfo.InvariantCulture);
        }

        Mod.Say(map == ArenaKind.Here
            ? $"[Where] {scene} is not a chart map (gym, ring, pit)"
            : $"[Where] \"arena\": {{ \"map\": \"{map.ToString().ToLowerInvariant()}\", \"at\": {{ \"x\": {F(at.x)}, \"z\": {F(at.z)} }}, \"facing\": {Arena.Bearing(rig.Facing).ToString("0", CultureInfo.InvariantCulture)} }}");
    }

    private void ReloadCharts()
    {
        _clips.Reload();
        foreach (var error in _clips.Errors) Mod.Say($"clip {error}");

        var written = _library.Reload();
        if (written > 0) Mod.Say($"wrote {written} starter chart(s) to {_library.Folder}");

        foreach (ChartEntry entry in _library.Entries.Where(e => !e.Result.Ok))
            Mod.Say($"chart {Path.GetFileName(entry.File)} has problems:{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", entry.Result.Errors)}");

        _settings.UpdateChartChoices(PlayableNames());
        foreach (ChartEntry entry in _library.Entries.Where(e => e.Source == ChartSource.Package && e.Result.Ok))
            Mod.Note("Charts.Package", $"{Path.GetFileName(entry.File)}: {entry.Name}", echo: false);
    }

    private void ToggleRecording()
    {
        if (!_recorder.Recording)
        {
            _recorder.Start();
            return;
        }

        Clip? clip = _recorder.Stop();
        if (clip == null) return;

        var path = _clips.Save(clip);
        Mod.Say($"[Clips] saved {clip.Name}: {clip.Duration:0.0}s, {clip.Frames.Count} frames -> {path}");
    }

    private IEnumerable<string> PlayableNames() => _library.Playable.Select(e => e.Name);

    private void ApplySettings()
    {
        Mod.Battle.CountdownSeconds = Math.Max(0f, _settings.Countdown.Value);
        Mod.Battle.ActorKnockbackTaken = Math.Clamp(_settings.ActorKnockbackTaken.Value, 0f, 1f);
        Mod.Battle.ShowBackstop = _settings.ShowBackstop.Value;
        Mod.Battle.ShowPhaseBanners = _settings.ShowPhaseBanners.Value;
        Mod.Battle.MusicVolume = Math.Clamp(_settings.MusicVolume.Value, 0f, 1f);
        Mod.Battle.VisualsStrength = _settings.ChartVisuals.Value;
        _picker.Open = _settings.ShowPicker.Value;
        _picker.Distance = Math.Clamp(_settings.PickerDistance.Value, 0.3f, 2f);
        _picker.Height = Math.Clamp(_settings.PickerHeight.Value, -1f, 0.5f);
        _picker.Side = Math.Clamp(_settings.PickerSide.Value, -1f, 1f);
        _picker.FixedInGym = _settings.PickerFixedInGym.Value;
    }

    private void HandleKeys()
    {
        if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl)) return;
        var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (Input.GetKeyDown(KeyCode.B) && shift)
        {
            IfEditing(() => RecordChosen());
        }
        else if (Input.GetKeyDown(KeyCode.B))
        {
            if (Mod.Studio.State == StudioState.Recording) Mod.Studio.Stop();
            else if (Mod.Battle.State == BattleState.Idle) StartBattle();
            else Mod.Battle.Stop("stopped from the keyboard");
        }
        else if (Input.GetKeyDown(KeyCode.N))
        {
            StepChart(shift ? -1 : 1);
        }
        else if (Input.GetKeyDown(KeyCode.M))
        {
            ToggleOrSummonPicker();
        }
        else if (Input.GetKeyDown(KeyCode.E) && shift)
        {
            IfEditing(OpenEditor);
        }
        else if (Input.GetKeyDown(KeyCode.E))
        {
            IfEditing(ExportChosen);
        }
        else if (Input.GetKeyDown(KeyCode.C) && shift)
        {
            IfEditing(() => NewChart());
        }
        else if (Input.GetKeyDown(KeyCode.W))
        {
            IfEditing(WhereAmI);
        }
        else if (Input.GetKeyDown(KeyCode.A))
        {
            NextArena();
        }
        else if (Input.GetKeyDown(KeyCode.R) && shift)
        {
            ToggleRecording();
        }
        else if (Input.GetKeyDown(KeyCode.R))
        {
            ReloadCharts();
        }
        else if (Input.GetKeyDown(KeyCode.G))
        {
            Mod.Battle.ReturnToGym();
        }
        else if (Input.GetKeyDown(KeyCode.F))
        {
            _desktopCamera.Toggle();
        }
        else if (Input.GetKeyDown(KeyCode.T))
        {
            TestBench.ToggleActor(puppet: shift); // PUPPET (shift)
        }
        else if (Input.GetKeyDown(KeyCode.Alpha9))
        {
            TestBench.ToggleControl();
        }
        else if (Input.GetKeyDown(KeyCode.P) && shift)
        {
            TestBench.PlayClip(_clips.All.OrderBy(c => c.Name, StringComparer.Ordinal).LastOrDefault());
        }
        else if (Input.GetKeyDown(KeyCode.P))
        {
            TestBench.TogglePlant();
        }
        else if (Input.GetKeyDown(KeyCode.K))
        {
            TestBench.CycleKnockback();
        }
        else if (Input.GetKeyDown(KeyCode.Alpha0))
        {
            Mod.Report("on request");
        }
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

    private void NextArena()
    {
        var count = Enum.GetValues<Settings.ArenaChoice>().Length;
        _settings.Arena.Value = (Settings.ArenaChoice)(((int)_settings.Arena.Value + 1) % count);
        Mod.Say($"arena: {_settings.Arena.Value}");
    }

    /// <summary>Opens the picker in front of you; if it is already in front of you, closes it.</summary>
    private void ToggleOrSummonPicker()
    {
        if (_picker.Visible && _settings.ShowPicker.Value)
        {
            _settings.ShowPicker.Value = false;
            return;
        }

        _settings.ShowPicker.Value = true;
        _picker.Summon();
    }
}
