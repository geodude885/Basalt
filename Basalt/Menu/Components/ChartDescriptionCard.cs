using Basalt.Presentation;
using Il2CppTMPro;
using UnityEngine;

namespace Basalt.Menu.Components;

internal class ChartDescriptionCard
{
    private const float RockDepth = 0.04f;
    private const float CoverSize = 0.32f;

    private readonly Covers _covers;
    private string? _coverPath;
    private Texture2D? _coverTexture;

    public ChartDescriptionCard(Transform menu, GameAssets assets, Covers covers)
    {
        _covers = covers;
        assets.CreateRockPanel("Card", menu, new Vector3(0f, 0.17f, 0.01f + (RockDepth / 2f)), new Vector3(0.8f, 0.42f, RockDepth));

        Text = assets.CreateText(menu, new Vector3(0f, 0.17f, 0f), new Vector2(0.74f, 0.38f), 1.2f, GameAssets.TextOnRock);
        Text.enableAutoSizing = true;
        Text.fontSizeMin = 0.3f;
        Text.fontSizeMax = 1.2f;
        Text.enableWordWrapping = true;

        Cover = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Cover.name = "Cover";
        UnityEngine.Object.Destroy(Cover.GetComponent<Collider>());
        Cover.transform.SetParent(menu, false);
        Cover.transform.localPosition = new Vector3(-0.2f, 0.17f, 0.005f);
        Cover.SetActive(false);
    }

    public TextMeshPro Text { get; }

    public GameObject Cover { get; }

    public void Show(MenuCard card)
    {
        Text.text = card.Text;
        ShowCover(card.CoverPath);
    }

    // Scaled to the image's shape; without one, the text has the whole card.
    private void ShowCover(string? path)
    {
        var texture = path == null ? null : _covers.Get(path);
        if (path == _coverPath && texture == _coverTexture) return;
        _coverPath = path;
        _coverTexture = texture;

        var material = texture == null ? null : _covers.Material(texture);
        var show = texture != null && material != null;
        Cover.SetActive(show);

        if (show)
        {
            Cover.GetComponent<Renderer>().sharedMaterial = material;
            var aspect = texture!.width / (float)Math.Max(1, texture.height);
            Cover.transform.localScale = aspect >= 1f ? new Vector3(CoverSize, CoverSize / aspect, 1f) : new Vector3(CoverSize * aspect, CoverSize, 1f);
        }

        Text.transform.localPosition = new Vector3(show ? 0.17f : 0f, 0.17f, 0f);
        Text.rectTransform.sizeDelta = new Vector2(show ? 0.42f : 0.74f, 0.38f);
    }
}
