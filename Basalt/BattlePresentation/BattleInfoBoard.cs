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

        _text!.text = text;
        FitSlabToText();
        GameObject!.SetActive(true);
        _hideAt = seconds is float s ? Time.time + s : float.PositiveInfinity;
    }

    public void ShowResult(BattleResult result, string? best = null)
    {
        var outcome = result.Survived ? "SURVIVED" : "DEFEATED";
        Show(
            $"{result.Chart}\n" +
            $"{outcome}   rank {result.Rank}\n" +
            $"score {result.Score}{(best == null ? "" : $"   <size=70%>{best}</size>")}\n" +
            $"<size=70%>damage taken {result.DamageTaken} ({result.HitsTaken} hits)  |  " +
            $"boss damage {result.ActorDamage:0.#}  |  {result.Seconds:0.0}s</size>");
    }

    public void Hide()
    {
        GameObject?.SetActive(false);
        _hideAt = float.PositiveInfinity;
    }

    public void Update()
    {
        if (GameObject != null && Time.time >= _hideAt) Hide();
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
        _hideAt = float.PositiveInfinity;
    }
}
