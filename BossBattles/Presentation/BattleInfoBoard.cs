using BossBattles.Services.Scoring;
using BossBattles.UI;
using Il2CppTMPro;
using UnityEngine;

namespace BossBattles.Presentation;

internal class BattleInfoBoard
{
    private const float SlabDepth = 0.06f;
    private const float SlabPadding = 0.12f;

    public GameObject? GameObject { get; private set; }
    public float FontSize { get; set; } = 1.6f;
    private static readonly Color TextOnRock = new(0.12f, 0.1f, 0.09f);

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
            $"<b>{result.Chart}</b>\n" +
            $"{outcome}   rank <b>{result.Rank}</b>\n" +
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

    public void Forget()
    {
        GameObject = null;
        _text = null;
        _slab = null;
        _hideAt = float.PositiveInfinity;
    }

    private void FitSlabToText()
    {
        _slab ??= GamePanels.CreateRockPanel("Slab", GameObject!.transform, Vector3.zero, Vector3.one);
        _text!.color = _slab == null ? Color.white : TextOnRock;
        if (_slab == null) return;

        var textSize = _text.GetPreferredValues(_text.text);
        _slab.transform.localScale = new Vector3(textSize.x + (2f * SlabPadding), textSize.y + (2f * SlabPadding), SlabDepth);
        _slab.transform.localPosition = new Vector3(0f, 0f, SlabDepth / 2f);
    }

    private bool EnsureExists()
    {
        if (GameObject != null && _text != null) return true;

        try
        {
            GameObject = new GameObject("BossBattles_Board");
            var textHolder = new GameObject("Text");
            textHolder.transform.SetParent(GameObject.transform, false);
            textHolder.transform.localPosition = new Vector3(0f, 0f, -0.01f);

            _text = textHolder.AddComponent<TextMeshPro>();
            _text.alignment = TextAlignmentOptions.Center;
            _text.fontSize = FontSize;
            _text.enableWordWrapping = false;
            _text.color = Color.white;
            Paint.GiveFont(_text);
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
}
