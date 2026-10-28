using Basalt.Battle;
using Basalt.Menu.Components;
using Basalt.Menu.Screens;
using Basalt.BattlePresentation;
using Basalt.Services;
using UnityEngine;

namespace Basalt.Menu;

internal class BasaltMenu(MenuScreens screens, JsonBook<SavedSpot> spots, GameAssets assets)
{
    public GameObject? Root { get; private set; }
    public ChartDescriptionCard? SelectedChartDescription { get; private set; }
    public ButtonRow? ButtonRow { get; private set; }
    public StepSlider? StepSlider { get; private set; }

    private const float RefreshSeconds = 0.25f;

    private IMenuScreen? _screen;
    private readonly MenuMover _mover = new(spots);
    private readonly Covers _covers = new(assets);
    private float _nextRefresh;


    public void Update()
    {
        var screen = BattlePlayer.Player?.Controller != null ? screens.Current : null;

        if (screen == null)
        {
            _screen = null;
            if (Root != null && Root.activeSelf) Root!.SetActive(false);
            return;
        }

        if (Root == null || !Root.activeSelf)
        {
            if (!EnsureExists()) return;
            _mover.PlaceOnOpening(Root!.transform, screen.Placement);
            ButtonRow!.HasMovedOrBecomeVisible();
            Root.SetActive(true);
            _nextRefresh = 0f;
        }

        _screen = screen;

        if (Time.time >= _nextRefresh) Refresh();

        if (screen.FollowsPlayer && MenuMover.UpdateFollowPlayer(Root!.transform, screen.Placement))
            ButtonRow!.HasMovedOrBecomeVisible();

        ButtonRow!.Update();
    }

    public void Summon()
    {
        if (Root == null || !Root.activeSelf || _screen == null) return;

        MenuMover.PlaceNearPlayer(Root!.transform, _screen.Placement);
        ButtonRow!.HasMovedOrBecomeVisible();
        _mover.SaveGymSpot(Root.transform);
    }

    private void Refresh()
    {
        _nextRefresh = Time.time + RefreshSeconds;
        SelectedChartDescription!.Show(_screen!.Card);
        StepSlider!.Show(_screen!.Slider);
        ButtonRow!.Show(_screen.Buttons);
    }

    private bool EnsureExists()
    {
        if (Root != null) return true;
        if (!assets.MenuPartsLoaded) return false;

        try
        {
            Root = new GameObject("Basalt_Menu");
            Root.SetActive(false);
            SelectedChartDescription = new ChartDescriptionCard(Root.transform, assets, _covers);
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
    public void Forget()
    {
        Root = null;
        SelectedChartDescription = null;
        ButtonRow = null;
        StepSlider = null;
        _screen = null;
    }
}
