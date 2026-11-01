namespace Basalt.Services.Charts.Model;

public record ActorPerformance(string ActorId, IReadOnlyList<PerformanceClip> Clips)
{
    internal static List<ActorPerformance> AllFrom(ChartDto dto, string defaultActorId, TempoMap? tempo)
        => (dto.Performances ?? []).Select(p => ReadPerformance(p, defaultActorId, tempo)).ToList();

    private static ActorPerformance ReadPerformance(PerformanceDto performance, string defaultActorId, TempoMap? tempo) =>
        new(performance.Actor ?? defaultActorId, (performance.Clips ?? []).Select(c => ReadClip(c, tempo)).ToList());

    private static PerformanceClip ReadClip(PerformanceClipDto clip, TempoMap? tempo) =>
        new(
            (clip.Beat is { } beats ? tempo?.BeatToSeconds(beats) : clip.T)!.Value,
            clip.Clip!,
            clip.Start ?? 0,
            clip.End)
        { Mime = clip.Mime ?? false };
}
