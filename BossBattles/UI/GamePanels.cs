using UnityEngine;

namespace BossBattles.UI;

// The Gym menu slab's rock panel, copied whole the first time it is seen and kept across scene loads, so the
// board and picker look like the game's own menus. A fresh renderer with the same mesh and material draws
// nothing: the game's shader depends on how its own renderer is set up, so the object itself is copied.
internal static class GamePanels
{
    private const string MenuSlabPanelPath = "SnapRotationDeadzoneSetting/Mesh";

    private static GameObject? _rockTemplate;
    private static Bounds _rockBounds;
    private static bool _reportedMissing;

    // A holder scaled to `size` like a primitive cube of that size, wearing the rock panel; its face is towards -Z.
    // Null until the Gym's menu slab has been seen.
    public static GameObject? CreateRockPanel(string name, Transform parent, Vector3 localCentre, Vector3 size)
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

    public static bool CaptureRockTemplate()
    {
        if (_rockTemplate != null) return true;

        try
        {
            var panel = Resources.FindObjectsOfTypeAll<MeshFilter>()
                .FirstOrDefault(f => f != null && f.sharedMesh != null && f.gameObject.scene.IsValid() && PathOf(f.transform).EndsWith(MenuSlabPanelPath, StringComparison.Ordinal));
            if (panel == null)
            {
                if (!_reportedMissing) Logger.Debug("[GamePanels.NoTemplate] no menu slab panel in this scene - flat panels until the Gym is visited");
                _reportedMissing = true;
                return false;
            }

            _rockTemplate = UnityEngine.Object.Instantiate(panel.gameObject);
            _rockTemplate.name = "BossBattles_RockPanelTemplate";
            _rockTemplate.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(_rockTemplate);
            foreach (var collider in _rockTemplate.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(collider);
            _rockBounds = panel.sharedMesh.bounds;
            Logger.Debug($"[GamePanels.Template] copied {PathOf(panel.transform)}: mesh bounds {_rockBounds.size.Describe()} centre {_rockBounds.center.Describe()}, layer {panel.gameObject.layer}");
            return true;
        }
        catch (Exception e)
        {
            if (!_reportedMissing) Logger.Error($"[GamePanels.Failed] {e}");
            _reportedMissing = true;
            return false;
        }
    }

    private static string PathOf(Transform transform)
    {
        var names = new List<string>();
        for (var t = transform; t != null; t = t.parent) names.Add(t.name);
        names.Reverse();
        return string.Join("/", names);
    }
}
