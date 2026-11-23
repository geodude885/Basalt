using Basalt.Services.Modes;
using Basalt.Services.Scoring;
using Il2CppTMPro;
using UnityEngine;

namespace Basalt.BattlePresentation;

internal class BattleInfoBoard(GameAssets assets)
{
    public GameObject? GameObject { get; private set; }
    public float FontSize { get; set; } = 1.6f;

    private const float SlabDepth = 0.06f;
    private const float SlabPadding = 0.12f;

    private TextMeshPro? _text;
    private GameObject? _slab;
    private float _hideAt = float.PositiveInfinity;
    private Func<float, string>? _animated;

    public void SetPosition(Vector3 position, Vector3 lookingFrom)
    {
        if (!EnsureExists()) return;

        var away = position - lookingFrom;
        away.y = 0f;
        GameObject!.transform.SetPositionAndRotation(position, Quaternion.LookRotation(away.sqrMagnitude > 1e-4f ? away : Vector3.forward));
    }

    public void Show(string text, float? seconds = null)
    {
        if (!EnsureExists()) return;

        _animated = null;
        _text!.text = text;
        FitSlabToText();
        GameObject!.SetActive(true);
        _hideAt = seconds is float s ? Time.time + s : float.PositiveInfinity;
    }

    public void ShowResult(BattleResult result, BestScore? previousBest = null, bool isNewBest = false, GameMode mode = GameMode.Normal)
    {
        var survived = result.Survived ? "SURVIVED" : "DEFEATED";
        var outcome = mode == GameMode.Normal ? survived
            : $"{mode.ToString().ToUpperInvariant()} {survived}";
        string Text(float time)
        {
            var best = !mode.SavesScores() ? "Not saved"
                : isNewBest ? "New Best"
                    : previousBest == null ? null
                        : $"Best: {RankLook.Markup(previousBest.Rank, time, false)} {previousBest.Score}";
            return $"{result.Chart}\n" +
                $"{outcome} Rank: {RankLook.Markup(result.Rank, time)}\n" +
                $"Score: {result.Score}\n" +
                $"{(best == null ? "" : $"<size=50%>{best}</size>")}\n" +
                $"<size=70%>Damage taken: {result.DamageTaken}</size>\n" +
                $"<size=70%>Boss damage: {result.ActorDamage:0.#}</size>";
        }

        Show(Text(0f));
        if (RankLook.IsAnimated(result.Rank) || (previousBest != null && RankLook.IsAnimated(previousBest.Rank))) _animated = Text;
    }

    public void Hide()
    {
        GameObject?.SetActive(false);
        _animated = null;
        _hideAt = float.PositiveInfinity;
    }

    public void Update()
    {
        if (GameObject != null && Time.time >= _hideAt)
            Hide();

        if (_animated != null && GameObject != null && GameObject.activeSelf)
            _text!.text = _animated(Time.time);
    }

    private bool EnsureExists()
    {
        if (GameObject != null && _text != null) return true;

        try
        {
            GameObject = new GameObject("Basalt_Board");
            _text = assets.CreateText(GameObject.transform, new Vector3(0f, 0f, -0.01f), null, FontSize, Color.white);
            return true;
        }
        catch (Exception e)
        {
            Logger.Error($"[Board.Failed] {e}");
            GameObject = null;
            _text = null;
            return false;
        }
    }
    private void FitSlabToText()
    {
        _slab ??= assets.CreateRockPanel("Slab", GameObject!.transform, Vector3.zero, Vector3.one);
        _text!.color = _slab == null ? Color.white : GameAssets.TextOnRock;
        if (_slab == null) return;

        var textSize = _text.GetPreferredValues(_text.text);
        _slab.transform.localScale = new Vector3(textSize.x + (2f * SlabPadding), textSize.y + (2f * SlabPadding), SlabDepth);
        _slab.transform.localPosition = new Vector3(0f, 0f, SlabDepth / 2f);
    }

    public void Forget()
    {
        GameObject = null;
        _text = null;
        _slab = null;
        _animated = null;
        _hideAt = float.PositiveInfinity;
    }
}
