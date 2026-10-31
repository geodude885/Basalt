namespace Basalt.Services.Charts.Model;

public readonly record struct Lane(float Length, float Width)
{
    public const float MinLength = 3f;
    public const float MaxLength = 40f;
    public const float MinWidth = 1f;
    public const float MaxWidth = 8f;

    public (Lane Lane, IReadOnlyList<ActorSpec> Actors) Resize(IReadOnlyList<ActorSpec> actors, float length, float width)
    {
        var oldLength = Length;
        var resized = new Lane(Math.Clamp(MathF.Round(length, 2), MinLength, MaxLength), Math.Clamp(MathF.Round(width, 2), MinWidth, MaxWidth));
        var moved = actors.Select(a => Math.Abs(a.At.Z - oldLength) < 0.01f || a.At.Z > resized.Length ? a with { At = a.At with { Z = resized.Length } } : a).ToList();
        return (resized, moved);
    }
}
