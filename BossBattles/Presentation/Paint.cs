using Il2CppTMPro;
using UnityEngine;

namespace BossBattles.Presentation;

/// <summary>
/// Materials for the mod's own shapes. A primitive's default shader is not in the game build (it
/// draws magenta), so one the game has loaded is borrowed.
/// </summary>
internal static class Paint
{
    private static readonly string[] Preferred =
    {
        "Universal Render Pipeline/Unlit",
        "Universal Render Pipeline/Simple Lit",
        "Universal Render Pipeline/Lit",
        "Unlit/Color",
        "Sprites/Default",
    };

    private static readonly string[] PreferredFonts =
    {
        "GOODDP__ SDF Global Text Material + DropShadow",
        "GoodDogPlain",
    };

    private static TMP_FontAsset? _font;
    private static Shader? _shader;
    private static bool _searched;

    /// <summary>A flat colour; null when no usable shader is loaded.</summary>
    public static Material? Colour(Color colour)
    {
        var shader = FindShader();
        if (shader == null) return null;

        var material = new Material(shader);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
        if (material.HasProperty("_Color")) material.SetColor("_Color", colour);
        return material;
    }

    /// <summary>A see-through, double-sided colour: URP Unlit on its transparent surface, even when opaque, so it can fade.</summary>
    public static Material? Glass(Color colour)
    {
        var material = Colour(colour);
        if (material == null) return material;

        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return material;
    }

    // The game's own handwriting (Good Dog Plain) with its drop shadow, then the plain cut, then whatever is loaded.
    public static void GiveFont(TextMeshPro text)
    {
        if (_font == null)
        {
            var loadedFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().Where(f => f != null).ToList();
            _font = PreferredFonts
                .Select(name => loadedFonts.FirstOrDefault(f => f.name == name))
                .FirstOrDefault(f => f != null) ?? loadedFonts.FirstOrDefault();
            if (_font == null)
            {
                Logger.Warning("[Paint.NoFont] no TextMeshPro font loaded - text will be blank");
                return;
            }

            Logger.Debug($"[Paint.Font] using {_font.name}");
        }

        text.font = _font;
    }

    private static Shader? FindShader()
    {
        if (_searched) return _shader;
        _searched = true;

        foreach (var name in Preferred)
        {
            var found = Shader.Find(name);
            if (found == null) continue;

            _shader = found;
            Logger.Debug($"[Paint.Shader] using {name}");
            return _shader;
        }

        _shader = Resources.FindObjectsOfTypeAll<Shader>()
            .FirstOrDefault(s => s != null && s.name.Contains("Unlit", StringComparison.OrdinalIgnoreCase));
        if (_shader == null) Logger.Warning("[Paint.Shader] no usable shader loaded - shapes keep their default material");
        else Logger.Debug($"[Paint.Shader] using loaded {_shader.name}");
        return _shader;
    }
}
