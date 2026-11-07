using Basalt.Services.Charts.Model;

namespace Basalt.Services.Charts;

public record PackLook(bool On, IReadOnlyDictionary<string, float> Values);

public static class CurrentVisuals
{
    public static Dictionary<string, PackLook> Evaluate(IReadOnlyList<ShaderCue> cues, double time, Func<string, string, float> baseline) =>
        new LookTimeline(cues, baseline).VisualsAt(time).ToDictionary(
            look => look.Key,
            look => new PackLook(look.Value.On, new Dictionary<string, float>(look.Value.Values, StringComparer.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);
}
