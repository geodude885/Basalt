using Basalt.Services.Charts.Model;

namespace Basalt.Services.Charts.Files;

internal static class ChartDtoExtensions
{
    public static Lane ToLane(this ChartDto dto) => new(
        dto.Lane?.Length ?? Lane.Default.Length,
        dto.Lane?.Width ?? Lane.Default.Width,
        dto.Lane?.Gutter ?? Lane.DefaultGutter);

    public static TempoMap? ToTempo(this ChartDto dto)
    {
        if (dto.Bpm is not > 0) return null;

        var sections = new List<TempoSection> { new(0, dto.Bpm.Value) };
        foreach (var change in dto.BpmChanges ?? [])
        {
            if (change.Beat > sections[^1].Beat && change.Bpm > 0) sections.Add(new TempoSection(change.Beat.Value, change.Bpm.Value));
        }

        return new TempoMap(sections);
    }

    public static List<string> ActorIds(this ChartDto dto) =>
        dto.Actors is { Count: > 0 } actors ? actors.Select(IdAt).ToList() : [ActorSpec.DefaultId];

    public static string IdAt(this ActorDto actor, int index) =>
        !string.IsNullOrWhiteSpace(actor.Id) ? actor.Id : index == 0 ? ActorSpec.DefaultId : $"actor{index + 1}";
}
