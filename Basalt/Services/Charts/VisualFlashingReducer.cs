namespace Basalt.Services.Charts;

public sealed class VisualFlashingReducer
{
    private readonly Dictionary<string, Dictionary<string, float>> _shown = new(StringComparer.OrdinalIgnoreCase);

    public float Delay { get; set; } // 0 For normal pack behaviour

    public void Reset() => _shown.Clear();

    public float WithFlashingReduction(string pack, string key, float target, float deltaTime, float start)
    {
        if (!_shown.TryGetValue(pack, out var values)) _shown[pack] = values = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        if (Delay <= 0f)
        {
            values[key] = target;
            return target;
        }

        var shown = values.TryGetValue(key, out var was) ? was : start;
        shown += (target - shown) * (1f - MathF.Exp(-Math.Max(0f, deltaTime) / Delay));
        values[key] = shown;
        return shown;
    }
}
