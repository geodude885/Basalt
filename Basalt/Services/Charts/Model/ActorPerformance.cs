namespace Basalt.Services.Charts.Model;

public record ActorPerformance(string ActorId, IReadOnlyList<PerformanceClip> Clips);
