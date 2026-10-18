using BossBattles.Presentation;
using UnityEngine;

namespace BossBattles.UI;

// Chart cover images (PNG or JPG), cut to a rough rock shape and cached until the file changes.
internal static class Covers
{
    // Room for a 2048 px PNG saved from the editor.
    private const long MaxBytes = 16 * 1024 * 1024;

    private const int MaskedCoverSize = 512;
    private const string MaskResource = "BossBattles.CoverMask.png";

    private static readonly Dictionary<string, (DateTime Written, Texture2D? Texture)> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static (int Side, byte[] Alpha)? _mask;
    private static bool _maskMissing;

    public static Texture2D? Get(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) return Remember(path, DateTime.MinValue, null, $"no cover at {path}");
            if (Cache.TryGetValue(path, out var cached) && cached.Written == file.LastWriteTimeUtc) return cached.Texture;
            if (file.Length > MaxBytes) return Remember(path, file.LastWriteTimeUtc, null, $"{file.Name} is over {MaxBytes / (1024 * 1024)} MB - not shown");

            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = file.Name, wrapMode = TextureWrapMode.Clamp };
            if (!ImageConversion.LoadImage(image, File.ReadAllBytes(path)))
            {
                UnityEngine.Object.Destroy(image);
                return Remember(path, file.LastWriteTimeUtc, null, $"{file.Name} is not a PNG or JPG");
            }

            var cover = CutToRockShape(image);
            Logger.Debug($"[Cover.Loaded] {file.Name}: {image.width}x{image.height}, shown {cover.width}x{cover.height}");
            if (cover != image) UnityEngine.Object.Destroy(image);
            cover.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return Remember(path, file.LastWriteTimeUtc, cover, null);
        }
        catch (Exception e)
        {
            return Remember(path, DateTime.MinValue, null, $"{Path.GetFileName(path)}: {e.Message}");
        }
    }

    // Unlit and see-through, so the mask's soft edge blends into the panel behind it.
    public static Material? Material(Texture2D texture)
    {
        var material = Paint.Glass(Color.white);
        if (material == null) return null;

        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
        return material;
    }

    // The centre square of the image, at most MaskedCoverSize across, with the mask multiplied into its alpha.
    private static Texture2D CutToRockShape(Texture2D image)
    {
        if (LoadMask() is not { } mask) return image;

        var side = Math.Min(image.width, image.height);
        var left = (image.width - side) / 2;
        var bottom = (image.height - side) / 2;
        var size = Math.Min(MaskedCoverSize, side);

        var source = image.GetPixels32();
        var cut = new Color32[size * size];
        foreach (var y in Enumerable.Range(0, size))
        {
            var sourceRow = (bottom + (y * side / size)) * image.width;
            var maskRow = y * mask.Side / size * mask.Side;
            foreach (var x in Enumerable.Range(0, size))
            {
                var pixel = source[sourceRow + left + (x * side / size)];
                pixel.a = (byte)(pixel.a * mask.Alpha[maskRow + (x * mask.Side / size)] / 255);
                cut[(y * size) + x] = pixel;
            }
        }

        var cover = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = image.name, wrapMode = TextureWrapMode.Clamp };
        cover.SetPixels32(cut);
        cover.Apply(false, true);
        return cover;
    }

    private static (int Side, byte[] Alpha)? LoadMask()
    {
        if (_mask != null || _maskMissing) return _mask;

        using var stream = typeof(Covers).Assembly.GetManifestResourceStream(MaskResource);
        if (stream == null)
        {
            _maskMissing = true;
            Logger.Warning($"[Cover.NoMask] {MaskResource} is not in the assembly - covers are shown square");
            return null;
        }

        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        ImageConversion.LoadImage(texture, bytes.ToArray());
        var pixels = texture.GetPixels32();
        _mask = (texture.width, Enumerable.Range(0, pixels.Length).Select(i => pixels[i].a).ToArray());
        UnityEngine.Object.Destroy(texture);
        return _mask;
    }

    private static Texture2D? Remember(string path, DateTime written, Texture2D? texture, string? problem)
    {
        var known = Cache.TryGetValue(path, out var before) && before.Written == written && before.Texture == null && texture == null;
        if (Cache.TryGetValue(path, out var old) && old.Texture != null && old.Texture != texture) UnityEngine.Object.Destroy(old.Texture);

        Cache[path] = (written, texture);
        if (problem != null && !known) Logger.Error($"[Cover.Failed] {problem}");
        return texture;
    }
}
