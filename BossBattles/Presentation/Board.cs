using BossBattles.Services.Scoring;
using Il2CppTMPro;
using UnityEngine;

namespace BossBattles.Presentation;

/// <summary>Floating text over the lane: the countdown, phase names and results.</summary>
internal sealed class Board
{
    private const float FontSize = 1.6f;

    private GameObject? _holder;
    private TextMeshPro? _text;
    private float _hideAt = float.PositiveInfinity;

    public void Place(Vector3 position, Vector3 lookingFrom)
    {
        if (!Ensure()) return;

        Vector3 away = position - lookingFrom;
        away.y = 0f;
        _holder!.transform.SetPositionAndRotation(position, Quaternion.LookRotation(away.sqrMagnitude > 1e-4f ? away : Vector3.forward));
    }

    public void Show(string text, float seconds = float.PositiveInfinity)
    {
        if (!Ensure()) return;

        _text!.text = text;
        _holder!.SetActive(true);
        _hideAt = float.IsPositiveInfinity(seconds) ? float.PositiveInfinity : Time.time + seconds;
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
        _holder?.SetActive(false);
        _hideAt = float.PositiveInfinity;
    }

    public void Update()
    {
        if (_holder != null && Time.time >= _hideAt) Hide();
    }

    /// <summary>For a scene change: the board went with the scene.</summary>
    public void Forget()
    {
        _holder = null;
        _text = null;
        _hideAt = float.PositiveInfinity;
    }

    private bool Ensure()
    {
        if (_holder != null && _text != null) return true;

        try
        {
            _holder = new GameObject("BossBattles_Board");
            _text = _holder.AddComponent<TextMeshPro>();
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
            _holder = null;
            _text = null;
            return false;
        }
    }
}
