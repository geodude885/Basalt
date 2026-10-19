using Il2CppRUMBLE.Interactions.InteractionBase;
using Il2CppTMPro;
using UnityEngine;

namespace Basalt.Presentation;

// What the mod borrows from the game to draw with: a shader, a font, and copies of the Gym's rock panel and push button.
// A primitive's default shader is not in the game build (it draws magenta), and a fresh renderer with the game's mesh and
// material draws nothing - so materials use a shader the game has loaded, and the panel and button are copied whole.
internal class GameAssets
{
    private const string MenuSlabPanelPath = "SnapRotationDeadzoneSetting/Mesh";

    private static readonly string[] PreferredShaders =
    {
        "Universal Render Pipeline/Unlit",
        "Universal Render Pipeline/Simple Lit",
        "Universal Render Pipeline/Lit",
        "Unlit/Color",
        "Sprites/Default",
    };

    // The game's own handwriting (Good Dog Plain) with its drop shadow, then the plain cut.
    private static readonly string[] PreferredFonts =
    {
        "GOODDP__ SDF Global Text Material + DropShadow",
        "GoodDogPlain",
    };

    private Shader? _shader;
    private bool _searchedForShader;
    private TMP_FontAsset? _font;
    private GameObject? _rockTemplate;
    private Bounds _rockBounds;
    private bool _reportedNoRock;
    private GameObject? _buttonTemplate;
    private bool _reportedNoButton;

    // Null when no usable shader is loaded.
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

    public void GiveFont(TextMeshPro text)
    {
        if (_font == null)
        {
            var loadedFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().Where(f => f != null).ToList();
            _font = PreferredFonts
                .Select(name => loadedFonts.FirstOrDefault(f => f.name == name))
                .FirstOrDefault(f => f != null) ?? loadedFonts.FirstOrDefault();
            if (_font == null)
            {
                Logger.Warning("[Assets.NoFont] no TextMeshPro font loaded - text will be blank");
                return;
            }

            Logger.Debug($"[Assets.Font] using {_font.name}");
        }

        text.font = _font;
    }

    // A holder scaled to `size` like a primitive cube of that size, wearing the rock panel; its face is towards -Z.
    // Null until the Gym's menu slab has been seen.
    public GameObject? CreateRockPanel(string name, Transform parent, Vector3 localCentre, Vector3 size)
    {
        if (!CaptureRockTemplate()) return null;

        var holder = new GameObject(name);
        holder.transform.SetParent(parent, false);
        holder.transform.localPosition = localCentre;
        holder.transform.localScale = size;

        // The game's mesh has its own size and pivot: fitted into the holder's unit box.
        var rock = UnityEngine.Object.Instantiate(_rockTemplate!, holder.transform, false);
        rock.name = "Rock";
        rock.transform.localRotation = Quaternion.identity;
        rock.transform.localScale = new Vector3(1f / _rockBounds.size.x, 1f / _rockBounds.size.y, 1f / _rockBounds.size.z);
        rock.transform.localPosition = Vector3.Scale(-_rockBounds.center, rock.transform.localScale);
        rock.SetActive(true);
        return holder;
    }

    // Copied the first time the Gym's menu slab is seen, and kept across scene loads.
    public bool CaptureRockTemplate()
    {
        if (_rockTemplate != null) return true;

        try
        {
            var panel = Resources.FindObjectsOfTypeAll<MeshFilter>()
                .FirstOrDefault(f => f != null && f.sharedMesh != null && f.gameObject.scene.IsValid() && PathOf(f.transform).EndsWith(MenuSlabPanelPath, StringComparison.Ordinal));
            if (panel == null)
            {
                if (!_reportedNoRock) Logger.Debug("[Assets.NoRockPanel] no menu slab panel in this scene - flat panels until the Gym is visited");
                _reportedNoRock = true;
                return false;
            }

            _rockTemplate = KeepCopyOf(panel.gameObject, "Basalt_RockPanelTemplate");
            foreach (var collider in _rockTemplate.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(collider);
            _rockBounds = panel.sharedMesh.bounds;
            Logger.Debug($"[Assets.RockPanel] copied {PathOf(panel.transform)}: mesh bounds {_rockBounds.size.Describe()} centre {_rockBounds.center.Describe()}, layer {panel.gameObject.layer}");
            return true;
        }
        catch (Exception e)
        {
            if (!_reportedNoRock) Logger.Error($"[Assets.RockPanelFailed] {e}");
            _reportedNoRock = true;
            return false;
        }
    }

    // The game's push button, from the Gym tutorial's "next page" button, so the mod's buttons press with the game's
    // feel and sound. Null until that button has been seen.
    public GameObject? CreateGameButton(Transform parent, Action onPressed)
    {
        if (!CaptureButtonTemplate()) return null;

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

    private bool CaptureButtonTemplate()
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
            if (next == null)
            {
                if (!_reportedNoButton) Logger.Debug($"[Assets.NoButton] no tutorial button in this scene ({buttons.Count} other buttons) - using the mod's own");
                _reportedNoButton = true;
                return false;
            }

            _buttonTemplate = KeepCopyOf(next.transform.parent.gameObject, "Basalt_ButtonTemplate");
            Logger.Debug($"[Assets.Button] copied {PathOf(next.transform.parent)} (scale {next.transform.parent.lossyScale.Describe()})");
            return true;
        }
        catch (Exception e)
        {
            if (!_reportedNoButton) Logger.Error($"[Assets.ButtonFailed] {e}");
            _reportedNoButton = true;
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
