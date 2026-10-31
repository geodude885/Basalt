namespace Basalt.Services.Charts.Model;

public record PerformanceClip(double Time, string Clip, double Start = 0, double? End = null)
{
    public bool Mime { get; init; }
}
