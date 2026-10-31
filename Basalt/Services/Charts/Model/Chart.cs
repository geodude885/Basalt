namespace Basalt.Services.Charts.Model;

// Loaded and validated chart, see ChartJson for json format
public record Chart(
    string Name,
    Lane Lane,
    IReadOnlyList<ActorSpec> Actors,
    IReadOnlyList<Phase> Phases,
    IReadOnlyList<ChartEvent> Events,
    double End)
{
    public const double DefaultEndPadding = 3.0;

    public ChartInfo Info { get; init; } = ChartInfo.None;
    public Rules Rules { get; init; } = Rules.Default;

    public ArenaSpec Arena { get; init; } = ArenaSpec.Default;
    public PlayerZone PlayerZone { get; init; } = PlayerZone.For(Lane.Default);


    public MusicSpec? Music { get; init; }
    public TempoMap? Tempo { get; init; }
    public string? Cover { get; init; } // PNG or JPG relative file path

    public ActorSpec DefaultActor => Actors[0];
    public IReadOnlyList<ActorPerformance> Performances { get; init; } = Array.Empty<ActorPerformance>();


    public LaneColours Colours { get; init; } = LaneColours.Default;
    public LaneColours ColoursIn(Phase? phase) => phase?.Colours?.Over(Colours) ?? Colours;
    public IReadOnlyList<ShaderCue> Shaders { get; init; } = Array.Empty<ShaderCue>();
}

public readonly record struct Spot(float X, float Z, float Facing = 0f);

public record Phase(string Name, double Time, double ScoreMultiplier)
{
    public LaneColours? Colours { get; init; }
}
