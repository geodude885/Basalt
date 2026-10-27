using Basalt.Battle;
using Basalt.Menu.Components;
using Basalt.Menu.Screens;
using Basalt.Presentation;
using Basalt.Services;
using UnityEngine;

namespace Basalt.Menu;

internal class BasaltMenu(MenuScreens screens, JsonBook<SavedSpot> spots, GameAssets assets)
{
    private const float RefreshSeconds = 0.25f;

    private readonly MenuMover _mover = new(spots);
    private readonly Covers _covers = new(assets);

    private IMenuScreen? _screen;
    private float _nextRefresh;

    // A card shown instead of the screen's.
    public MenuCard? Override { get; set; }

    public bool Visible => Root != null && Root.activeSelf;

    public GameObject? Root { get; private set; }

    public ChartDescriptionCard? Card { get; private set; }

    public ButtonRow? ButtonRow { get; private set; }

    public StepSlider? StepSlider { get; private set; }

    public void Update()
    {
        var screen = BattlePlayer.Player?.Controller != null ? screens.Current : null;
        if (screen == null)
        {
            _screen = null;
            if (Visible) Root!.SetActive(false);
            return;
        }

        if (!Visible)
        {
            if (!EnsureExists()) return;
            _mover.PlaceOnOpening(Root!.transform, screen.Placement);
            ButtonRow!.Arrived();
            Root.SetActive(true);
            _nextRefresh = 0f;
        }

        _screen = screen;
        if (Time.time >= _nextRefresh) Refresh();
        if (screen.FollowsPlayer && MenuMover.FollowPlayer(Root!.transform, screen.Placement)) ButtonRow!.Arrived();
        ButtonRow!.Update();
    }

    // Brings the menu to you; in the Gym this becomes its fixed spot.
    public void Summon()
    {
        if (!Visible || _screen == null) return;

        MenuMover.PlaceNearPlayer(Root!.transform, _screen.Placement);
        ButtonRow!.Arrived();
        _mover.SaveGymSpot(Root.transform);
    }

    // For a scene change: the menu went with the scene.
    public void Forget()
    {
        Root = null;
        Card = null;
        ButtonRow = null;
        StepSlider = null;
        _screen = null;
    }

    private void Refresh()
    {
        _nextRefresh = Time.time + RefreshSeconds;
        Card!.Show(Override ?? _screen!.Card);
        StepSlider!.Show(_screen!.Slider);
        ButtonRow!.Show(_screen.Buttons);
    }

    // Once the game's parts have been seen in the Gym.
    private bool EnsureExists()
    {
        if (Root != null) return true;
        if (!assets.HasMenuParts) return false;

        try
        {
            Root = new GameObject("Basalt_Menu");
            Root.SetActive(false);
            Card = new ChartDescriptionCard(Root.transform, assets, _covers);
            ButtonRow = new ButtonRow(Root.transform, assets, () => _nextRefresh = 0f);
            StepSlider = new StepSlider(Root.transform, assets);
            return true;
        }
        catch (Exception e)
        {
            Logger.Error($"[Menu.Failed] {e}");
            if (Root != null) UnityEngine.Object.Destroy(Root);
            Forget();
            return false;
        }
    }
}
