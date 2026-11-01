namespace Basalt.Services.Charts.Model;

public record ShaderCue(double Time, string Pack, IReadOnlyDictionary<string, float> Values, bool Off, double Fade)
{
    internal static List<ShaderCue> AllFrom(ChartDto dto, TempoMap? tempo)
        => (dto.Shaders ?? []).Select(shader => From(shader, tempo)).OrderBy(c => c.Time).ToList();

    private static ShaderCue From(ShaderDto shader, TempoMap? tempo)
    {
        var values = new Dictionary<string, float>(shader.Values ?? [], StringComparer.OrdinalIgnoreCase);

        return new ShaderCue(
            (shader.Beat is { } beats ? tempo?.BeatToSeconds(beats) : shader.T)!.Value,
            shader.Pack!.Trim(),
            values, shader.Off ?? false,
            shader.Fade ?? 0);
    }

}
