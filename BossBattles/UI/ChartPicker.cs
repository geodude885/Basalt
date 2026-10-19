using BossBattles.Actors;
using BossBattles.Arenas;
using BossBattles.Battle;
using BossBattles.Presentation;
using BossBattles.Services;
using BossBattles.Services.Charts;
using Il2CppRUMBLE.Interactions.InteractionBase;
using Il2CppTMPro;
using UnityEngine;

namespace BossBattles.UI;

/// <summary>What the picker's card shows: rich text, and a cover image file or none.</summary>
internal record PickerCard(string Text, string? CoverPath = null);

/// <summary>A button on the picker; the id is for keys and tests.</summary>
internal record PickerButton(string Id, string Label, Color Colour, Action Press);

/// <summary>Where the picker stands when it appears.</summary>
internal enum PickerPlacement
{
    /// <summary>In front of you by the placement settings, or at its fixed spot in the Gym.</summary>
    InFront,

    /// <summary>Off to your left, out of the way while recording.</summary>
    Beside,
}

/// <summary>
/// The mod's panel in the world: a card and the buttons its host supplies for the moment. Buttons
/// are the game's own push buttons (<see cref="GameAssets.CreateGameButton"/>) once one has been seen, else the
/// mod's boxes poked by hand position; they can also be clicked or pressed from the keys. Built
/// without colliders, so nothing of it touches the game's physics.
/// </summary>
internal class ChartPicker(PickerPanel panel, JsonBook<SavedSpot> spots, GameAssets assets)
{
    private const float PokeMargin = 0.03f;
    private const float PressCooldown = 0.5f;

    /// <summary>After the panel moves its buttons wait this long: it can land right where a hand is.</summary>
    private const float ArrivalLockout = 1f;
    private const float PressedSeconds = 0.15f;
    private const float RefreshSeconds = 0.25f;
    private const float CoverSize = 0.32f;
    private const float ButtonWidth = 0.15f;
    private const float ButtonGap = 0.04f;
    private const int ButtonsPerRow = 5;
    private const float RowSpacing = 0.21f;

    private const float RockDepth = 0.04f;
    private const float ColourBarHeight = 0.014f;

    private static readonly Color CardColour = new(0.08f, 0.08f, 0.1f);
    private static readonly Color PressedColour = new(1f, 0.9f, 0.4f);
    private static readonly Color TextOnRock = new(0.12f, 0.1f, 0.09f);

    private readonly List<Button> _buttons = [];
    private readonly Covers _covers = new(assets);

    private GameObject? _root;
    private TextMeshPro? _card;
    private GameObject? _cover;
    private string? _coverPath;
    private Texture2D? _coverTexture;
    private string _buttonKey = "";
    private bool _shown;
    private float _nextRefresh;
    private float _cooldownUntil;
    private float _arrivedAt = float.NegativeInfinity;
    private bool _leftWasIn;
    private bool _rightWasIn;

    /// <summary>Whether the picker is wanted between battles.</summary>
    public bool Open { get; set; } = true;

    /// <summary>Metres in front of your eyes.</summary>
    public float Distance { get; set; } = 0.75f;

    /// <summary>Metres above your eyes; negative is below.</summary>
    public float Height { get; set; } = -0.35f;

    /// <summary>Metres to your right; negative is left.</summary>
    public float Side { get; set; }

    /// <summary>Keeps a fixed spot in the Gym once one is set, rather than coming to you.</summary>
    public bool FixedInGym { get; set; } = true;

    /// <summary>A card shown instead of the host's.</summary>
    public PickerCard? Override { get; set; }

    public bool ShowingCover => _cover != null && _cover.activeSelf;

    public bool Visible => _root != null && _root.activeSelf;

    public string CardText => _card == null ? "" : _card.text;

    public IEnumerable<string> ButtonIds => _buttons.Select(b => b.Spec.Id);

    /// <summary>The showing buttons' faces, unit boxes in their own space.</summary>
    public IEnumerable<(string Id, Transform Face)> ShownButtons =>
        _shown ? _buttons.Where(b => b.Root.activeSelf).Select(b => (b.Spec.Id, b.Face)) : [];

    public Transform? Root => _root?.transform;

    public void Update()
    {
        var want = panel.Wanted(Open) && BattlePlayer.Controller != null;

        if (want && !_shown)
        {
            if (!Ensure()) return;
            if (panel.Placement == PickerPlacement.Beside || !PlaceAtSavedSpot()) PlaceInFrontOfPlayer();
            _root!.SetActive(true);
            _nextRefresh = 0f;
        }
        else if (!want && _shown && _root != null)
        {
            _root.SetActive(false);
        }

        _shown = want && _root != null;
        if (!_shown) return;

        if (Time.time >= _nextRefresh) Refresh();
        // Following you: brought back when you walk away from it.
        if (panel.FollowsPlayer && BattlePlayer.Controller is { } playerController)
        {
            var away = _root!.transform.position - ActorRig.From(playerController).Head.position;
            away.y = 0f;
            if (away.magnitude > 1.6f) PlaceInFrontOfPlayer();
        }

        foreach (var button in _buttons) button.Settle();
        CheckPokes();
    }

    /// <summary>Brings the picker to you, opening it; in the Gym this becomes its fixed spot.</summary>
    public void Summon()
    {
        Open = true;
        if (_root == null || !_shown) return;

        PlaceInFrontOfPlayer();
        if (!FixedInGym || !ArenaKind.Gym.IsLoaded()) return;

        var position = _root.transform.position;
        try
        {
            spots.Set(Maps.GymScene, new SavedSpot(position.x, position.y, position.z, _root.transform.eulerAngles.y));
            Logger.Debug($"[Picker.Moved] {position.Describe()}");
        }
        catch (IOException e)
        {
            Logger.Error($"[Picker.SaveFailed] {e.Message}");
        }
    }

    /// <summary>The same as pushing the button; false when no such button shows.</summary>
    public bool Press(string id)
    {
        var button = _buttons.FirstOrDefault(b => b.Spec.Id == id);
        if (button == null) return false;

        button.Flash();
        Logger.Debug($"[Picker.Pressed] {id}");
        _nextRefresh = 0f;
        button.Spec.Press();
        return true;
    }

    /// <summary>Fires a game button's own <c>onPressed</c>, as a hand pushing it would; false for the mod's own buttons.</summary>
    public bool InvokeGameButton(string id)
    {
        var button = _buttons.FirstOrDefault(b => b.Spec.Id == id);
        if (button is not { Native: true }) return false;

        var interaction = button.Root.GetComponentInChildren<InteractionButton>(true);
        if (interaction == null) return false;

        interaction.onPressed.Invoke();
        return true;
    }

    /// <summary>For a scene change: the picker went with the scene.</summary>
    public void Forget()
    {
        _root = null;
        _card = null;
        _cover = null;
        _coverPath = null;
        _coverTexture = null;
        _buttons.Clear();
        _buttonKey = "";
        _shown = false;
    }

    private void Refresh()
    {
        _nextRefresh = Time.time + RefreshSeconds;

        var card = Override ?? panel.Card();
        _card!.text = card.Text;
        ShowCover(card.CoverPath);

        var wanted = panel.Buttons();
        var key = string.Join("|", wanted.Select(b => $"{b.Id}:{b.Label}"));
        if (key != _buttonKey)
        {
            BuildButtons(wanted, key);
        }
        else
        {
            // Same buttons; their actions may close over new state.
            foreach ((var button, var spec) in _buttons.Zip(wanted)) button.Spec = spec;
        }
    }

    private void BuildButtons(IReadOnlyList<PickerButton> specs, string key)
    {
        foreach (var old in _buttons) UnityEngine.Object.Destroy(old.Root);
        _buttons.Clear();
        _buttonKey = key;

        // Rows of up to five, each centred, the first under the card.
        foreach ((var inRow, var row) in specs.Chunk(ButtonsPerRow).Select((r, i) => (r, i)))
        {
            var total = (inRow.Length * ButtonWidth) + ((inRow.Length - 1) * ButtonGap);
            foreach ((var spec, var i) in inRow.Select((s, i) => (s, i)))
                AddButton(spec, (-total / 2f) + (ButtonWidth / 2f) + (i * (ButtonWidth + ButtonGap)), -row * RowSpacing);
        }

        _leftWasIn = _rightWasIn = true;
    }

    /// <summary>The cover beside the text, scaled to its shape; the text clips the whole card without one.</summary>
    private void ShowCover(string? path)
    {
        if (_cover == null || _card == null) return;

        var texture = path == null ? null : _covers.Get(path);
        if (path == _coverPath && texture == _coverTexture) return;
        _coverPath = path;
        _coverTexture = texture;

        var material = texture == null ? null : _covers.Material(texture);
        var show = texture != null && material != null;
        _cover.SetActive(show);

        if (show)
        {
            _cover.GetComponent<Renderer>().sharedMaterial = material;
            var aspect = texture!.width / (float)Math.Max(1, texture.height);
            _cover.transform.localScale = aspect >= 1f ? new Vector3(CoverSize, CoverSize / aspect, 1f) : new Vector3(CoverSize * aspect, CoverSize, 1f);
        }

        _card.transform.localPosition = new Vector3(show ? 0.17f : 0f, 0.17f, 0f);
        _card.rectTransform.sizeDelta = new Vector2(show ? 0.42f : 0.74f, 0.38f);
    }

    /// <summary>In the Gym, its fixed spot; false anywhere else, when it has none, or when it is not wanted.</summary>
    private bool PlaceAtSavedSpot()
    {
        if (_root == null || !FixedInGym || !ArenaKind.Gym.IsLoaded() || spots.Get(Maps.GymScene) is not { } spot) return false;

        _root.transform.SetPositionAndRotation(new Vector3(spot.X, spot.Y, spot.Z), Quaternion.Euler(0f, spot.Yaw, 0f));
        _leftWasIn = _rightWasIn = true;
        return true;
    }

    private void CheckPokes()
    {
        var playerController = BattlePlayer.Controller;
        if (playerController == null) return;

        var rig = ActorRig.From(playerController);
        var left = ButtonAt(rig.LeftHand.position);
        var right = ButtonAt(rig.RightHand.position);

        // A press is a hand arriving at a button, not resting on one.
        if (left != null && !_leftWasIn) TryPress(left);
        else if (right != null && !_rightWasIn) TryPress(right);

        _leftWasIn = left != null;
        _rightWasIn = right != null;
    }

    private void TryPress(Button button)
    {
        if (Time.time < _cooldownUntil || Time.time < _arrivedAt + ArrivalLockout) return;
        _cooldownUntil = Time.time + PressCooldown;
        Press(button.Spec.Id);
    }

    private Button? ButtonAt(Vector3 point) =>
        _buttons.FirstOrDefault(b => !b.Native && b.Root.activeSelf && b.Contains(point, PokeMargin));

    private void PlaceInFrontOfPlayer()
    {
        var playerController = BattlePlayer.Controller;
        if (playerController == null || _root == null) return;

        var rig = ActorRig.From(playerController);
        var facing = rig.Facing;
        var right = Vector3.Cross(Vector3.up, facing);

        var position = panel.Placement == PickerPlacement.Beside
            ? rig.Head.position + (facing * 0.35f) - (right * 0.8f) + (Vector3.down * 0.45f)
            : rig.Head.position + (facing * Distance) + (Vector3.up * Height) + (right * Side);

        var away = position - rig.Head.position;
        _root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(away.Flat()));
        _leftWasIn = _rightWasIn = true; // no press from hands already where it appears
        _arrivedAt = Time.time;
    }

    private bool Ensure()
    {
        if (_root != null) return true;

        try
        {
            _root = new GameObject("BossBattles_ChartPicker");

            var onRock = assets.CreateRockPanel("Card", _root.transform, new Vector3(0f, 0.17f, 0.01f + (RockDepth / 2f)), new Vector3(0.8f, 0.42f, RockDepth)) != null;
            if (!onRock) Box(_root.transform, "Card", new Vector3(0f, 0.17f, 0.02f), new Vector3(0.8f, 0.42f, 0.02f), CardColour);
            _card = Text(_root.transform, new Vector3(0f, 0.17f, 0f), new Vector2(0.74f, 0.38f), 1.2f, onRock ? TextOnRock : Color.white);
            _card.enableAutoSizing = true;
            _card.fontSizeMin = 0.3f;
            _card.fontSizeMax = 1.2f;
            _card.enableWordWrapping = true;

            _cover = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _cover.name = "Cover";
            var coverCollider = _cover.GetComponent<Collider>();
            if (coverCollider != null) UnityEngine.Object.Destroy(coverCollider);
            _cover.transform.SetParent(_root.transform, false);
            _cover.transform.localPosition = new Vector3(-0.2f, 0.17f, 0.005f);
            _cover.SetActive(false);

            Refresh();
            Logger.Debug($"[Picker.Built] chart picker built with {(_buttons.Any(b => b.Native) ? "the game's buttons" : "the mod's own buttons")}");
            return true;
        }
        catch (Exception e)
        {
            Logger.Error($"[Picker.Failed] {e}");
            if (_root != null) UnityEngine.Object.Destroy(_root);
            Forget();
            return false;
        }
    }

    private void AddButton(PickerButton spec, float x, float rowOffset)
    {
        var holder = new GameObject($"Button_{spec.Id}");
        holder.transform.SetParent(_root!.transform, false);
        holder.transform.localPosition = new Vector3(x, -0.12f + rowOffset, -0.04f);

        Button? made = null;
        var native = assets.CreateGameButton(holder.transform, () =>
        {
            if (made != null && made.Root != null && made.Root.activeInHierarchy && _buttons.Contains(made)) TryPress(made);
        });

        if (native != null)
        {
            native.transform.localPosition = new Vector3(0f, -0.02f, -0.02f);
            native.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        }

        var faceCentre = native != null ? new Vector3(0f, 0.01f, 0.01f) : Vector3.zero;
        var faceSize = native != null ? new Vector3(ButtonWidth, 0.16f, 0.02f) : new Vector3(ButtonWidth, 0.09f, 0.05f);
        var labelCentre = native != null ? new Vector3(0f, 0.058f, -0.005f) : new Vector3(0f, 0f, -0.03f);

        // On a rock tile the button's colour is a bar along its top edge; without one, the whole face is coloured.
        var rockFace = assets.CreateRockPanel("Face", holder.transform, faceCentre, faceSize);
        var face = rockFace ?? Box(holder.transform, "Face", faceCentre, faceSize, spec.Colour);
        var colourBar = rockFace == null
            ? face
            : Box(rockFace.transform, "ColourBar", new Vector3(0f, 0.5f - (ColourBarHeight / faceSize.y / 2f), -0.5f), new Vector3(1f, ColourBarHeight / faceSize.y, 0.2f), spec.Colour);
        var onRock = rockFace != null;

        var text = Text(holder.transform, labelCentre, new Vector2(ButtonWidth, native != null ? 0.05f : 0.09f), native != null ? 0.5f : 0.6f, onRock ? TextOnRock : Color.white);

        text.enableAutoSizing = true;
        text.fontSizeMin = 0.2f;
        text.fontSizeMax = 0.6f;
        text.text = $"{spec.Label}";

        made = new Button(spec, holder, face, colourBar, spec.Colour, assets) { Native = native != null };
        _buttons.Add(made);
    }

    private GameObject Box(Transform parent, string name, Vector3 position, Vector3 size, Color colour)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        var collider = box.GetComponent<Collider>();
        if (collider != null) UnityEngine.Object.Destroy(collider);

        box.transform.SetParent(parent, false);
        box.transform.localPosition = position;
        box.transform.localScale = size;

        var renderer = box.GetComponent<Renderer>();
        var material = assets.Colour(colour);
        if (renderer != null && material != null) renderer.material = material;
        return box;
    }

    private TextMeshPro Text(Transform parent, Vector3 position, Vector2 size, float fontSize, Color colour)
    {
        var holder = new GameObject("Text");
        holder.transform.SetParent(parent, false);
        holder.transform.localPosition = position;

        var text = holder.AddComponent<TextMeshPro>();
        text.rectTransform.sizeDelta = size;
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = fontSize;
        text.enableWordWrapping = false;
        text.color = colour;

        assets.GiveFont(text);
        return text;
    }

    /// <summary>Whether a point is inside a primitive box, grown by a margin in metres.</summary>
    private static bool InBox(Transform box, Vector3 world, float margin)
    {
        var local = box.InverseTransformPoint(world);
        var scale = box.lossyScale;
        return Mathf.Abs(local.x) <= 0.5f + (margin / scale.x)
               && Mathf.Abs(local.y) <= 0.5f + (margin / scale.y)
               && Mathf.Abs(local.z) <= 0.5f + (margin / scale.z);
    }

    private class Button
    {
        private readonly GameObject _face;
        private readonly GameObject _colourBar;
        private readonly Material? _normal;
        private readonly Material? _pressed;
        private readonly Vector3 _rest;
        private float _releaseAt;

        public Button(PickerButton spec, GameObject root, GameObject face, GameObject colourBar, Color colour, GameAssets assets)
        {
            Spec = spec;
            Root = root;
            _face = face;
            _colourBar = colourBar;
            _rest = face.transform.localPosition;
            _normal = assets.Colour(colour);
            _pressed = assets.Colour(PressedColour);
        }

        public PickerButton Spec { get; set; }

        public GameObject Root { get; }

        /// <summary>A game button, which the game presses itself.</summary>
        public bool Native { get; init; }

        public Transform Face => _face.transform;

        public bool Contains(Vector3 world, float margin) => InBox(_face.transform, world, margin);

        public void Flash()
        {
            _releaseAt = Time.time + PressedSeconds;
            Recolour(_pressed);
            _face.transform.localPosition = _rest + new Vector3(0f, 0f, 0.02f);
        }

        public void Settle()
        {
            if (_releaseAt == 0f || Time.time < _releaseAt) return;
            _releaseAt = 0f;
            Recolour(_normal);
            _face.transform.localPosition = _rest;
        }

        private void Recolour(Material? material)
        {
            var renderer = _colourBar.GetComponent<Renderer>();
            if (renderer != null && material != null) renderer.sharedMaterial = material;
        }
    }
}
