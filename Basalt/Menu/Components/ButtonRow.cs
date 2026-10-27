using Basalt.Presentation;
using UnityEngine;

namespace Basalt.Menu.Components;

internal class ButtonRow(Transform menu, GameAssets assets, Action pressed)
{
    private const float ButtonWidth = 0.15f;
    private const float ButtonGap = 0.04f;
    private const int ButtonsPerRow = 5;
    private const float RowSpacing = 0.21f;
    private const float PressedSeconds = 0.15f;
    private const float PressCooldown = 0.5f;

    private const float AfterMenuMovedLockForSeconds = 1f;

    private static readonly Color PressedColour = new(1f, 0.9f, 0.4f);

    private string _key = "";
    private float _cooldownUntil;
    private float _arrivedAt = float.NegativeInfinity;

    public List<Button> Buttons { get; } = [];

    public void Show(IReadOnlyList<MenuButton> specs)
    {
        var key = string.Join("|", specs.Select(b => $"{b.Id}:{b.Label}:{b.Colour}"));
        if (key == _key)
        {
            foreach ((var button, var spec) in Buttons.Zip(specs)) button.Spec = spec;
            return;
        }

        foreach (var old in Buttons) UnityEngine.Object.Destroy(old.Root);
        Buttons.Clear();
        _key = key;

        foreach ((var inRow, var row) in specs.Chunk(ButtonsPerRow).Select((r, i) => (r, i)))
        {
            var total = (inRow.Length * ButtonWidth) + ((inRow.Length - 1) * ButtonGap);
            foreach ((var spec, var i) in inRow.Select((s, i) => (s, i)))
                Add(spec, (-total / 2f) + (ButtonWidth / 2f) + (i * (ButtonWidth + ButtonGap)), -row * RowSpacing);
        }
    }

    public void Arrived() => _arrivedAt = Time.time;

    public void Update()
    {
        foreach (var button in Buttons) button.Settle();
    }

    public bool Press(string id)
    {
        var button = Buttons.FirstOrDefault(b => b.Spec.Id == id);
        if (button == null) return false;

        button.Flash();
        Logger.Debug($"[Menu.Pressed] {id}");
        button.Spec.Press();
        pressed();
        return true;
    }

    private void PressedByHand(Button button)
    {
        if (!Buttons.Contains(button) || Time.time < _cooldownUntil || Time.time < _arrivedAt + AfterMenuMovedLockForSeconds) return;
        _cooldownUntil = Time.time + PressCooldown;
        Press(button.Spec.Id);
    }

    private void Add(MenuButton spec, float x, float rowOffset)
    {
        var holder = new GameObject($"Button_{spec.Id}");
        holder.transform.SetParent(menu, false);
        holder.transform.localPosition = new Vector3(x, -0.12f + rowOffset, -0.04f);

        Button? made = null;
        var game = assets.CreateGameButton(holder.transform, () =>
        {
            if (made != null) PressedByHand(made);
        });
        game!.transform.localPosition = new Vector3(0f, -0.02f, -0.02f);
        game.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        var cap = game.GetComponentsInChildren<Renderer>(true).First(r => r.name == "Button");

        var face = assets.CreateRockPanel("Face", holder.transform, new Vector3(0f, 0.01f, 0.01f), new Vector3(ButtonWidth, 0.16f, 0.02f))!;
        var label = assets.CreateText(holder.transform, new Vector3(0f, 0.058f, -0.005f), new Vector2(ButtonWidth, 0.05f), 0.5f, GameAssets.TextOnRock);
        label.enableAutoSizing = true;
        label.fontSizeMin = 0.2f;
        label.fontSizeMax = 0.6f;
        label.text = spec.Label;

        made = new Button(spec, holder, face, cap);
        Buttons.Add(made);
    }

    // The cap takes the button's colour through the game shader's overlay, which tints without a material of its own.
    public class Button
    {
        private readonly Renderer _cap;
        private readonly Vector3 _rest;
        private float _releaseAt;

        public Button(MenuButton spec, GameObject root, GameObject face, Renderer cap)
        {
            Spec = spec;
            Root = root;
            Face = face;
            _cap = cap;
            _rest = face.transform.localPosition;
            Tint(spec.Colour);
        }

        public MenuButton Spec { get; set; }

        public GameObject Root { get; }

        public GameObject Face { get; }

        public void Flash()
        {
            _releaseAt = Time.time + PressedSeconds;
            Tint(PressedColour);
            Face.transform.localPosition = _rest + new Vector3(0f, 0f, 0.02f);
        }

        public void Settle()
        {
            if (_releaseAt == 0f || Time.time < _releaseAt) return;
            _releaseAt = 0f;
            Tint(Spec.Colour);
            Face.transform.localPosition = _rest;
        }

        private void Tint(Color colour)
        {
            var overlay = new MaterialPropertyBlock();
            overlay.SetColor("_Overlay", colour with { a = 1f });
            _cap.SetPropertyBlock(overlay);
        }
    }
}
