using Basalt.Services.Charts.Files;

namespace Basalt.Services.Charts.Model;

public record ActorSpec(string Id, string Name, Spot At, string Look)
{
    public const string DefaultId = "actor";
    public const string DefaultLook = "you";

    public IReadOnlyList<string> Shiftstones { get; init; } = [];
    public BodySize? Measurement { get; init; }

    internal static List<ActorSpec> AllFrom(ChartDto dto, Lane lane)
    {
        if (dto.Actors is not { Count: > 0 } actors)
            return [new(DefaultId, "Actor", new Spot(0f, lane.Length), DefaultLook)];

        return actors.Select((actor, i) => ReadActor(actor, i, lane)).ToList();
    }

    private static ActorSpec ReadActor(ActorDto actor, int index, Lane lane)
    {
        var id = actor.IdAt(index);
        var name = string.IsNullOrWhiteSpace(actor.Name) ? id : actor.Name;
        var at = actor.At == null
            ? new Spot(0f, lane.Length)
            : new Spot(actor.At.X!.Value, actor.At.Z!.Value, actor.At.Facing ?? 0f);
        var look = string.IsNullOrWhiteSpace(actor.Look) ? DefaultLook : actor.Look;

        return new ActorSpec(id, name, at, look)
        {
            Shiftstones = actor.Shiftstones?.Select(s => s.Trim()).ToArray() ?? [],
            Measurement = actor.Measurement is { } size ? new BodySize(size.Height!.Value, size.ArmSpan!.Value) : null,
        };
    }

}

public readonly record struct BodySize(float Height, float ArmSpan);
