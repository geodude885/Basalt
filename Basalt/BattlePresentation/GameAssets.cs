using Il2CppRUMBLE.Interactions.InteractionBase;
using Il2CppTMPro;
using UnityEngine;

namespace Basalt.BattlePresentation;

internal class GameAssets
{
    private const string MenuSlabPanelPath = "SnapRotationDeadzoneSetting/Mesh";
    private const string MenuSlabSliderPath = "StickMovementDeadzone/Slider";

    private static readonly string[] PreferredShaders =
    {
        "Universal Render Pipeline/Unlit",
        "Universal Render Pipeline/Simple Lit",
        "Universal Render Pipeline/Lit",
        "Unlit/Color",
        "Sprites/Default",
    };

    private const string PreferredFont = "RUMBLE Text Material";

    public static readonly Color TextOnRock = new(0.12f, 0.1f, 0.09f);

    public bool MenuPartsLoaded => _rockTemplate != null && _buttonTemplate != null && _sliderTemplate != null;

    private Shader? _shader;
    private bool _searchedForShader;
    private TMP_FontAsset? _font;
    private GameObject? _rockTemplate;
    private Bounds _rockBounds;
    private GameObject? _buttonTemplate;
    private GameObject? _sliderTemplate;
    private Quaternion _sliderRotation;
    private Vector3 _sliderScale;

    public bool CaptureMenuParts() => EnsureRockPanelLoaded() & EnsureButtonLoaded() & EnsureSliderLoaded();

    public Material? Colour(Color colour)
    {
        var shader = FindShader();
        if (shader == null) return null;

        var material = new Material(shader);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
        if (material.HasProperty("_Color")) material.SetColor("_Color", colour);
        return material;
    }

    // See-through and double-sided: URP Unlit on its transparent surface, even when opaque, so it can fade.
    public Material? Glass(Color colour)
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

    public TextMeshPro CreateText(Transform parent, Vector3 position, Vector2? size, float fontSize, Color colour)
    {
        var holder = new GameObject("Text");
        holder.transform.SetParent(parent, false);
        holder.transform.localPosition = position;

        var text = holder.AddComponent<TextMeshPro>();
        if (size is { } fixedSize) text.rectTransform.sizeDelta = fixedSize;
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = fontSize;
        text.enableWordWrapping = false;
        text.color = colour;
        GiveFont(text);
        return text;
    }

    private void GiveFont(TextMeshPro text)
    {
        if (_font == null)
        {
            var loadedFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().Where(f => f != null).ToList();
            _font = loadedFonts.FirstOrDefault(f => f.name == PreferredFont) ?? loadedFonts.FirstOrDefault();
            if (_font == null)
            {
                Logger.Warning("[Assets.NoFont] no TextMeshPro font loaded - text will be blank");
                return;
            }

            Logger.Debug($"[Assets.Font] using {_font.name}");
        }

        text.font = _font;
    }

    public GameObject? CreateRockPanel(string name, Transform parent, Vector3 localCentre, Vector3 size)
    {
        if (!EnsureRockPanelLoaded()) return null;

        var holder = new GameObject(name);
        holder.transform.SetParent(parent, false);
        holder.transform.localPosition = localCentre;
        holder.transform.localScale = size;

        var rock = UnityEngine.Object.Instantiate(_rockTemplate!, holder.transform, false);
        rock.name = "Rock";
        rock.transform.localRotation = Quaternion.identity;
        rock.transform.localScale = new Vector3(1f / _rockBounds.size.x, 1f / _rockBounds.size.y, 1f / _rockBounds.size.z);
        rock.transform.localPosition = Vector3.Scale(-_rockBounds.center, rock.transform.localScale);
        rock.SetActive(true);
        return holder;
    }

    private bool EnsureRockPanelLoaded()
    {
        if (_rockTemplate != null) return true;

        try
        {
            var panel = Resources.FindObjectsOfTypeAll<MeshFilter>()
                .FirstOrDefault(f => f != null && f.sharedMesh != null && f.gameObject.scene.IsValid() && PathOf(f.transform).EndsWith(MenuSlabPanelPath, StringComparison.Ordinal));
            if (panel == null) return false;

            _rockTemplate = KeepCopyOf(panel.gameObject, "Basalt_RockPanelTemplate");
            foreach (var collider in _rockTemplate.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(collider);
            _rockBounds = panel.sharedMesh.bounds;
            Logger.Debug($"[Assets.RockPanel] copied {PathOf(panel.transform)}: mesh bounds {_rockBounds.size.Describe()} centre {_rockBounds.center.Describe()}, layer {panel.gameObject.layer}");
            return true;
        }
        catch (Exception e)
        {
            Logger.Error($"[Assets.RockPanelFailed] {e}");
            return false;
        }
    }

    // Inspiration from of @SpooderCode's RockUI
    public GameObject? CreateGameButton(Transform parent, Action onPressed)
    {
        if (!EnsureButtonLoaded()) return null;

        try
        {
            var button = UnityEngine.Object.Instantiate(_buttonTemplate!, parent, false);
            button.name = "GameButton";
            button.SetActive(true);

            var interaction = button.GetComponentInChildren<InteractionButton>(true);
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
            Logger.Error($"[Assets.ButtonFailed] {e}");
            return null;
        }
    }
    private bool EnsureButtonLoaded()
    {
        if (_buttonTemplate != null) return true;

        try
        {
            var buttons = Resources.FindObjectsOfTypeAll<InteractionButton>()
                .Where(b => b != null && b.gameObject.scene.IsValid() && b.transform.parent != null)
                .ToList();

            // The button object is the InteractionButton component's parent.
            var next = buttons.FirstOrDefault(b => PathOf(b.transform).Contains("Next", StringComparison.OrdinalIgnoreCase)
                                                   && PathOf(b.transform).Contains("tutorial", StringComparison.OrdinalIgnoreCase));
            if (next == null) return false;

            _buttonTemplate = KeepCopyOf(next.transform.parent.gameObject, "Basalt_ButtonTemplate");
            Logger.Debug($"[Assets.Button] copied {PathOf(next.transform.parent)} (scale {next.transform.parent.lossyScale.Describe()})");
            return true;
        }
        catch (Exception e)
        {
            Logger.Error($"[Assets.ButtonFailed] {e}");
            return false;
        }
    }

    // Inspiration from of @SpooderCode's RockUI
    public GameObject? CreateGameSlider(Transform parent, int steps, int step, Action<int> onStep)
    {
        if (!EnsureSliderLoaded()) return null;

        try
        {
            var slider = UnityEngine.Object.Instantiate(_sliderTemplate!, parent, false);
            slider.name = "GameSlider";
            slider.transform.localRotation = _sliderRotation;
            slider.transform.localScale = _sliderScale;
            slider.SetActive(true);

            var handle = slider.GetComponentInChildren<InteractionSlider>(true);
            if (handle == null)
            {
                UnityEngine.Object.Destroy(slider);
                return null;
            }

            handle.useSteps = true;
            handle.stepCount = steps;
            handle.SetStep(step, true, true);
            handle.onStepReached.AddListener(onStep);
            return slider;
        }
        catch (Exception e)
        {
            Logger.Error($"[Assets.SliderFailed] {e}");
            return null;
        }
    }

    private bool EnsureSliderLoaded()
    {
        if (_sliderTemplate != null) return true;

        try
        {
            var original = Resources.FindObjectsOfTypeAll<InteractionSlider>()
                .Select(handle => handle.transform.parent)
                .FirstOrDefault(slider => slider != null && slider.gameObject.scene.IsValid() && PathOf(slider).EndsWith(MenuSlabSliderPath, StringComparison.Ordinal));
            if (original == null) return false;

            var mesh = original.parent.Find("Mesh");
            _sliderRotation = Quaternion.Inverse(mesh != null ? mesh.localRotation : Quaternion.identity) * original.localRotation;
            _sliderScale = original.lossyScale;
            _sliderTemplate = KeepCopyOf(original.gameObject, "Basalt_SliderTemplate");

            foreach (var label in _sliderTemplate.GetComponentsInChildren<TMP_Text>(true)) label.gameObject.SetActive(false);

            Logger.Debug($"[Assets.Slider] copied {PathOf(original)} (scale {_sliderScale.Describe()})");
            return true;
        }
        catch (Exception e)
        {
            Logger.Error($"[Assets.SliderFailed] {e}");
            return false;
        }
    }

    private Shader? FindShader()
    {
        if (_searchedForShader) return _shader;
        _searchedForShader = true;

        _shader = PreferredShaders.Select(Shader.Find).FirstOrDefault(s => s != null)
                  ?? Resources.FindObjectsOfTypeAll<Shader>().FirstOrDefault(s => s != null && s.name.Contains("Unlit", StringComparison.OrdinalIgnoreCase));
        if (_shader == null) Logger.Warning("[Assets.NoShader] no usable shader loaded - shapes keep their default material");
        else Logger.Debug($"[Assets.Shader] using {_shader.name}");
        return _shader;
    }

    private static GameObject KeepCopyOf(GameObject original, string name)
    {
        var copy = UnityEngine.Object.Instantiate(original);
        copy.name = name;
        copy.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(copy);
        return copy;
    }

    private static string PathOf(Transform transform)
    {
        var names = new List<string>();
        for (var t = transform; t != null; t = t.parent) names.Add(t.name);
        names.Reverse();
        return string.Join("/", names);
    }
}
