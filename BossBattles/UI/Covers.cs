using BossBattles.Presentation;
using UnityEngine;

namespace BossBattles.UI;

/// <summary>Chart cover images (PNG or JPG) as textures, cached until the file changes.</summary>
internal static class Covers
{
    /// <summary>Room for a 2048 px PNG saved from the editor.</summary>
    private const long MaxBytes = 16 * 1024 * 1024;

    private static readonly Dictionary<string, (DateTime Written, Texture2D? Texture)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The cover at <paramref name="path"/>; null when missing, too big or not an image.</summary>
    public static Texture2D? Get(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) return Remember(path, DateTime.MinValue, null, $"no cover at {path}");
            if (Cache.TryGetValue(path, out (DateTime Written, Texture2D? Texture) cached) && cached.Written == file.LastWriteTimeUtc) return cached.Texture;
            if (file.Length > MaxBytes) return Remember(path, file.LastWriteTimeUtc, null, $"{file.Name} is over {MaxBytes / (1024 * 1024)} MB - not shown");

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = file.Name, wrapMode = TextureWrapMode.Clamp };
            if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path)))
            {
                UnityEngine.Object.Destroy(texture);
                return Remember(path, file.LastWriteTimeUtc, null, $"{file.Name} is not a PNG or JPG");
            }

            texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Logger.Debug($"[Cover.Loaded] {file.Name}: {texture.width}x{texture.height}");
            return Remember(path, file.LastWriteTimeUtc, texture, null);
        }
        catch (Exception e)
        {
            return Remember(path, DateTime.MinValue, null, $"{Path.GetFileName(path)}: {e.Message}");
        }
    }

    /// <summary>The texture unlit; null without a usable shader.</summary>
    public static Material? Material(Texture2D texture)
    {
        Material? material = Paint.Colour(Color.white);
        if (material == null) return null;

        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
        return material;
    }

    private static Texture2D? Remember(string path, DateTime written, Texture2D? texture, string? problem)
    {
        var known = Cache.TryGetValue(path, out (DateTime Written, Texture2D? Texture) before) && before.Written == written && before.Texture == null && texture == null;
        if (Cache.TryGetValue(path, out (DateTime Written, Texture2D? Texture) old) && old.Texture != null && old.Texture != texture) UnityEngine.Object.Destroy(old.Texture);

        Cache[path] = (written, texture);
        if (problem != null && !known) Logger.Error($"[Cover.Failed] {problem}");
        return texture;
    }
}
