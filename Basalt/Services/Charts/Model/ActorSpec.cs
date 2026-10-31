namespace Basalt.Services.Charts.Model;

public record ActorSpec(string Id, string Name, Spot At, string Look)
{
    public IReadOnlyList<string>? Shiftstones { get; init; }
    public BodySize? Measurement { get; init; }
}

public readonly record struct BodySize(float Height, float ArmSpan);
