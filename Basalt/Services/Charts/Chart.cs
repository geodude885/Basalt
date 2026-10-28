using System.Numerics;

namespace Basalt.Services.Charts;

/// <summary>
/// A loaded, validated chart, built by <see cref="ChartLoader"/>. Lane coordinates: x across from the
/// centre line, y up, z along from your end (0) to <see cref="Lane.Length"/>; times are seconds.
/// </summary>
public record Chart(
    string Name,
    Lane Lane,
    IReadOnlyList<ActorSpec> Actors,
    IReadOnlyList<Phase> Phases,
    IReadOnlyList<ChartEvent> Events,
    double End)
{
    /// <summary>The actor events and phases address when they name none.</summary>
    public ActorSpec DefaultActor => Actors[0];

    public ChartInfo Info { get; init; } = ChartInfo.None;

    public ArenaSpec Arena { get; init; } = ArenaSpec.Default;

    /// <summary>A song played from GO, which the chart's clock follows.</summary>
    public MusicSpec? Music { get; init; }

    /// <summary>A PNG or JPG beside the chart, shown in the picker.</summary>
    public string? Cover { get; init; }

    /// <summary>The chart's tempo, when it times things in beats.</summary>
    public TempoMap? Tempo { get; init; }

    /// <summary>Recorded performances, one track per actor.</summary>
    public IReadOnlyList<Track> Tracks { get; init; } = Array.Empty<Track>();

    /// <summary>How far down the lane and how high you may go.</summary>
    public Zone Zone { get; init; } = Zone.For(new Lane(ChartLoader.DefaultLaneLength, ChartLoader.DefaultLaneWidth));

    public Rules Rules { get; init; } = Rules.Default;

    public LaneColours Colours { get; init; } = LaneColours.Default;

    /// <summary>The lane's colours during <paramref name="phase"/>: its own over the chart's.</summary>
    public LaneColours ColoursIn(Phase? phase) => phase?.Colours?.Over(Colours) ?? Colours;

    /// <summary>Whether the walls are shown at any point; their panes are built only then.</summary>
    public bool EverShowsWalls => Colours.ShowsWalls || Phases.Any(p => p.Colours?.ShowsWalls == true);

    /// <summary>RumbleReShaded looks over time, in time order.</summary>
    public IReadOnlyList<VisualCue> Visuals { get; init; } = Array.Empty<VisualCue>();
}

/// <summary>Your part of the lane, walled off for you only: <see cref="Length"/> along it and <see cref="Height"/> up.</summary>
public readonly record struct Zone(float Length, float Height)
{
    public const float DefaultClearance = 2f;

    public const float DefaultHeight = 3.5f;

    /// <summary>The default: 2 m short of the far end (half a short lane), 3.5 m high.</summary>
    public static Zone For(Lane lane) =>
        new(lane.Length > DefaultClearance * 2 ? lane.Length - DefaultClearance : lane.Length / 2f, DefaultHeight);
}

/// <summary>Which structures you may spawn.</summary>
public enum SpawnRule
{
    All,
    Discs,
    None,
}

public record Rules(SpawnRule Spawn)
{
    public const double DefaultCleanupSeconds = 3;

    public static Rules Default { get; } = new(SpawnRule.All);

    /// <summary>A structure still for this long breaks; 0 never.</summary>
    public double CleanupSeconds { get; init; } = DefaultCleanupSeconds;
}

/// <summary>An actor's recorded performance: clips, each playing until the next starts or its own end.</summary>
public record Track(string ActorId, IReadOnlyList<TrackClip> Clips);

/// <summary>
/// A clip on a track, played from chart time <see cref="Time"/>: the clip from <see cref="From"/> to
/// <see cref="To"/> or its end. Clips keep chart time, so a whole clip is <c>{ t: 0, from: 0 }</c>.
/// </summary>
public record TrackClip(double Time, string Clip, double From = 0, double? To = null)
{
    /// <summary>Mimed: the actor moves as recorded but runs none of its moves.</summary>
    public bool Mime { get; init; }
}

/// <summary>A chart's description for the picker; every part optional.</summary>
public record ChartInfo(string? Author, string? Description, string? Difficulty)
{
    public static ChartInfo None { get; } = new(null, null, null);
}

public enum ArenaKind
{
    // The Gym, in world coordinates.
    Gym,

    // The Ring (Map0), from its centre.
    Ring,

    // The Pit (Map1), from its centre.
    Pit,
}

// Where a chart is played: you stand at X, Z metres from the map's centre (the Gym: the world origin), and the lane
// runs along compass Facing. A chart that names a map but no spot starts at its centre, facing north.
public record ArenaSpec(ArenaKind Map, float X = 0f, float Z = 0f, float Facing = 0f)
{
    // For a chart with no arena at all: the Ring, 6 m south of its centre facing north, which a 12 m lane fits across.
    public static ArenaSpec Default { get; } = new(ArenaKind.Ring, 0f, -6f, 0f);
}

/// <summary>
/// A song beside the chart; <see cref="Start"/> is how far into it chart time 0 is - negative when the
/// song starts after chart time 0, with silence before it.
/// </summary>
public record MusicSpec(string File, double Start = 0, float Volume = 1f);

public readonly record struct Lane(float Length, float Width);

/// <summary>An actor in the lane. <see cref="Look"/> is "you", "default", "defaultFemale", or a look string as the game stores it.</summary>
public record ActorSpec(string Id, string Name, Spot At, string Look)
{
    /// <summary>Up to two stones by name; null for the player's own.</summary>
    public IReadOnlyList<string>? Shiftstones { get; init; }

    /// <summary>The body size of whoever recorded its clips, so their poses fit it; null for the player's own.</summary>
    public BodySize? Measurement { get; init; }
}

/// <summary>A player's calibrated size in metres, as the game measures it.</summary>
public readonly record struct BodySize(float Height, float ArmSpan);

/// <summary>Where an actor stands; Facing is degrees from looking at you, positive to its right.</summary>
public readonly record struct Spot(float X, float Z, float Facing = 0f);

/// <summary>A timed section of the battle, scoring <see cref="ScoreMultiplier"/>; it may move one actor.</summary>
public record Phase(string Name, double Start, double ScoreMultiplier, string? ActorId, Spot? At)
{
    /// <summary>The lane's colours from this phase on, over the chart's own; null keeps them.</summary>
    public LaneColours? Colours { get; init; }
}

/// <summary><see cref="Order"/> is the event's position in the chart, breaking ties in time.</summary>
public abstract record ChartEvent(double Time, int Order);

/// <summary>
/// A structure that appears at <see cref="From"/> <see cref="Telegraph"/> seconds early, then flies
/// at <see cref="Speed"/> towards <see cref="To"/>; <see cref="By"/> credits it to an actor.
/// </summary>
public record ProjectileEvent(
    double Time,
    int Order,
    string Structure,
    Vector3 From,
    Aim To,
    float Speed,
    float Telegraph,
    string? By) : ChartEvent(Time, Order)
{
    public double SpawnTime => Time - Telegraph;

    /// <summary>
    /// In the game's grounded state (more damage), driven along the floor at its target; null for the
    /// structure's default.
    /// </summary>
    public bool? Grounded { get; init; }

    public static bool GroundedByDefault(string structure) =>
        structure.Equals("Pillar", StringComparison.OrdinalIgnoreCase)
        || structure.Equals("RockCube", StringComparison.OrdinalIgnoreCase)
        || structure.Equals("Wall", StringComparison.OrdinalIgnoreCase);

    public bool IsGrounded => Grounded ?? GroundedByDefault(Structure);

    // The game brings these up out of the floor rather than into the air: they rise from it, and keep their facing
    // in flight - turned along their path, a disc or wall would tip its edge into the floor.
    public bool SpawnsFromFloor => FloorSpawned.Contains(Structure, StringComparer.OrdinalIgnoreCase);

    // On the floor whatever height the chart gave: spawned from it, or thrown along it.
    public bool RestsOnFloor => SpawnsFromFloor || IsGrounded;

    private static readonly string[] FloorSpawned = ["Disc", "Pillar", "RockCube", "Wall"];
}

/// <summary>An actor performs one of its moves by stack name, e.g. "Straight".</summary>
public record MoveEvent(double Time, int Order, string ActorId, string Stack) : ChartEvent(Time, Order);

public record PlaceEvent(double Time, int Order, string ActorId, Spot At) : ChartEvent(Time, Order);

/// <summary>A point on your end of the lane, or wherever you are.</summary>
public readonly record struct Aim(bool AtPlayer, float X, float Y)
{
    public static Aim Player => new(true, 0f, 0f);

    public static Aim Point(float x, float y) => new(false, x, y);
}
