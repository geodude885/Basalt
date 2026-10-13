using Il2CppRUMBLE.Interactions.InteractionBase;
using UnityEngine;

namespace BossBattles.UI;

/// <summary>
/// The game's own push button, copied from the Gym tutorial's "next page" button the first time it
/// is seen and kept across scene loads, so the mod's buttons press with the game's feel and sound.
/// </summary>
internal static class GameButtons
{
    private static GameObject? _template;
    private static bool _reported;

    /// <summary>A game button under <paramref name="parent"/>; null until a template has been seen.</summary>
    public static GameObject? Create(Transform parent, Action onPressed)
    {
        if (!EnsureTemplate()) return null;

        try
        {
            GameObject button = UnityEngine.Object.Instantiate(_template!, parent, false);
            button.name = "GameButton";
            button.SetActive(true);

            InteractionButton? interaction = button.GetComponentInChildren<InteractionButton>(true);
            if (interaction == null)
            {
                UnityEngine.Object.Destroy(button);
                return null;
            }

            interaction.onPressed.AddListener(onPressed);
            return button;
        }
        catch (Exception e)
        {
            Logger.Error($"[GameButtons.Failed] {e}");
            return null;
        }
    }

    private static bool EnsureTemplate()
    {
        if (_template != null) return true;

        try
        {
            var buttons = Resources.FindObjectsOfTypeAll<InteractionButton>()
                .Where(b => b != null && b.gameObject.scene.IsValid() && b.transform.parent != null)
                .ToList();

            // The button object is the InteractionButton component's parent.
            InteractionButton? next = buttons.FirstOrDefault(b => PathOf(b.transform).Contains("Next", StringComparison.OrdinalIgnoreCase)
                                                                  && PathOf(b.transform).Contains("tutorial", StringComparison.OrdinalIgnoreCase));
            if (next == null)
            {
                if (!_reported) Logger.Debug($"[GameButtons.NoTemplate] no tutorial button in this scene ({buttons.Count} other buttons) - using the mod's own");
                _reported = true;
                return false;
            }

            _template = UnityEngine.Object.Instantiate(next.transform.parent.gameObject);
            _template.name = "BossBattles_ButtonTemplate";
            _template.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(_template);
            Logger.Debug($"[GameButtons.Template] copied {PathOf(next.transform.parent)} (scale {StringHelpers.Vector(next.transform.parent.lossyScale)})");
            return true;
        }
        catch (Exception e)
        {
            if (!_reported) Logger.Error($"[GameButtons.Failed] {e}");
            _reported = true;
            return false;
        }
    }

    private static string PathOf(Transform transform)
    {
        var names = new List<string>();
        for (Transform? t = transform; t != null; t = t.parent) names.Add(t.name);
        names.Reverse();
        return string.Join("/", names);
    }
}
