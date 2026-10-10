using System.Numerics;

namespace BossBattles.Services.Charts;

// A battle as data. Lane coordinates: x across (0 = centre line), y up, z along the lane from your
// end (0) to the far end (Lane.Length). Times are seconds from the start of the battle.

/// <summary>A loaded, validated chart. Build one with <see cref="ChartLoader"/>.</summary>
public sealed record Chart(
    string Name,
    Lane Lane,
    IReadOnlyList<ActorSpec> Actors,
    IReadOnlyList<Phase> Phases,
    IReadOnlyList<ChartEvent> Events,
    double End)
{
    /// <summary>The actor events and phases address when they name none.</summary>
    public ActorSpec DefaultActor => Actors[0];

    /// <summary>Who made it and what it is, for the chart picker.</summary>
    public ChartInfo Info { get; init; } = ChartInfo.None;

    /// <summary>Where it is played. Without one, wherever you stand.</summary>
    public ArenaSpec Arena { get; init; } = ArenaSpec.Here;

    /// <summary>A song played from GO, which the chart's clock then follows; null for none.</summary>
    public MusicSpec? Music { get; init; }

    /// <summary>A cover image beside the chart (PNG or JPG), shown in the picker; null for none.</summary>
    public string? Cover { get; init; }

    /// <summary>The chart's tempo, when it times things in beats - recording steps sections by a beat.</summary>
    public TempoMap? Tempo { get; init; }

    /// <summary>Recorded performances, one track per actor - what the recording tools write.</summary>
    public IReadOnlyList<Track> Tracks { get; init; } = Array.Empty<Track>();

    /// <summary>Where you may go: how far down the lane, how high. Defaults keep you off the actor's end.</summary>
    public Zone Zone { get; init; } = Zone.For(new Lane(ChartLoader.DefaultLaneLength, ChartLoader.DefaultLaneWidth));

    /// <summary>What you may do in the battle.</summary>
    public Rules Rules { get; init; } = Rules.Default;

    /// <summary>The lane's colours, and whether its walls are seen.</summary>
    public LaneColours Colours { get; init; } = LaneColours.Default;

    /// <summary>The lane's colours during <paramref name="phase"/>: its own over the chart's.</summary>
    public LaneColours ColoursIn(Phase? phase) => phase?.Colours?.Over(Colours) ?? Colours;

    /// <summary>Whether the walls are shown at any point - their panes are built only then.</summary>
    public bool EverShowsWalls => Colours.ShowsWalls || Phases.Any(p => p.Colours?.ShowsWalls == true);

    /// <summary>Shader looks over time (RumbleReShaded packs), in time order; empty for none.</summary>
    public IReadOnlyList<VisualCue> Visuals { get; init; } = Array.Empty<VisualCue>();
}

/// <summary>
/// Your part of the lane: from your end to <see cref="Length"/> along it (chart metres), and up to
/// <see cref="Height"/> above the floor. Invisible walls stop you - not structures, not actors.
/// </summary>
public readonly record struct Zone(float Length, float Height)
{
    /// <summary>How far short of the lane's far end you stop when a chart gives no zone length.</summary>
    public const float DefaultClearance = 2f;

    public const float DefaultHeight = 3.5f;

    /// <summary>The zone a chart gets without one: 2 m clear of the far end (half the lane on a short one), 3.5 m high.</summary>
    public static Zone For(Lane lane) =>
        new(lane.Length > DefaultClearance * 2 ? lane.Length - DefaultClearance : lane.Length / 2f, DefaultHeight);
}

/// <summary>Which structures you may spawn: all of them, only discs (to move with), or none - moves only.</summary>
public enum SpawnRule
{
    All,
    Discs,
    None,
}

public sealed record Rules(SpawnRule Spawn)
{
    public const double DefaultCleanupSeconds = 3;

    public static Rules Default { get; } = new(SpawnRule.All);

    /// <summary>A structure that sits still this long in a battle breaks - a wall grounded in front of you does not last the chart. 0: never.</summary>
    public double CleanupSeconds { get; init; } = DefaultCleanupSeconds;
}

/// <summary>An actor's recorded performance: takes, each playing until the next starts (or its own end).</summary>
public sealed record Track(string ActorId, IReadOnlyList<TakeSegment> Takes);

/// <summary>
/// Part of a recorded take played from chart time <see cref="Time"/>: the clip's own time
/// <see cref="From"/> to <see cref="To"/> (or its end). A take recorded against the chart keeps
/// chart time, so a whole take is <c>{ t: 0, from: 0 }</c>; re-recording a section adds a segment
/// over the part replaced, and the takes themselves are never rewritten.
/// </summary>
public sealed record TakeSegment(double Time, string Clip, double From = 0, double? To = null)
{
    /// <summary>
    /// Mimed: the actor moves as recorded but none of the take's moves run, so nothing spawns -
    /// a performance laid over the chart's own projectiles, recorded pretending to throw them.
    /// </summary>
    public bool Mime { get; init; }
}

/// <summary>A chart's description for players choosing one. Every part is optional.</summary>
public sealed record ChartInfo(string? Author, string? Description, string? Difficulty)
{
    public static ChartInfo None { get; } = new(null, null, null);
}

/// <summary>The maps a chart can be played on.</summary>
public enum ArenaKind
{
    /// <summary>Wherever you stand, the lane running the way you face.</summary>
    Here,

    /// <summary>The Gym, in world coordinates.</summary>
    Gym,

    /// <summary>The Ring (Map0), from its centre.</summary>
    Ring,

    /// <summary>The Pit (Map1), from its centre.</summary>
    Pit,
}

/// <summary>
/// Where a chart is played. <see cref="X"/>/<see cref="Z"/> is where you stand - metres from the
/// arena's centre (the Gym: world coordinates) - and <see cref="Facing"/> the compass direction the
/// lane runs from you, in degrees (0 = world +Z, 90 = +X). Without a spot the lane is laid across
/// the arena floor through its centre; without a facing it runs towards the centre.
/// </summary>
public sealed record ArenaSpec(ArenaKind Map, float? X = null, float? Z = null, float? Facing = null)
{
    public static ArenaSpec Here { get; } = new(ArenaKind.Here);

    public bool HasSpot => X != null && Z != null;
}

/// <summary>
/// A song beside the chart (in its folder or package). <see cref="Start"/> is how far into the song
/// chart time 0 is - skip an intro, or line beat 0 up with the first downbeat.
/// </summary>
public sealed record MusicSpec(string File, double Start = 0, float Volume = 1f);

public readonly record struct Lane(float Length, float Width);

/// <summary>
/// A game-simulated player the battle puts in the lane. <see cref="Look"/> is "you" (whoever plays,
/// the default), "default", "defaultFemale", or a character string as the game stores it - a chart
/// made in game stores its maker's.
/// </summary>
public sealed record ActorSpec(string Id, string Name, Spot At, string Look)
{
    /// <summary>The stones it wears, by name (up to two; empty for none) - or null for the player's own.</summary>
    public IReadOnlyList<string>? Shiftstones { get; init; }
}

/// <summary>Where an actor stands, planted. Facing is degrees from looking down the lane at you; positive turns to its right.</summary>
public readonly record struct Spot(float X, float Z, float Facing = 0f);

/// <summary>A timed section of the battle. Actor damage scores <see cref="ScoreMultiplier"/>; a phase may move one actor.</summary>
public sealed record Phase(string Name, double Start, double ScoreMultiplier, string? ActorId, Spot? At)
{
    /// <summary>The lane's colours from this phase on, over the chart's own; null keeps them.</summary>
    public LaneColours? Colours { get; init; }
}

/// <summary><see cref="Order"/> is the event's position in the chart, which breaks ties in time.</summary>
public abstract record ChartEvent(double Time, int Order);

/// <summary>
/// A structure that appears at <see cref="From"/> <see cref="Telegraph"/> seconds before
/// <see cref="ChartEvent.Time"/> - hovering as the warning - then flies at <see cref="Speed"/> towards
/// <see cref="To"/> on your end of the lane. <see cref="By"/> credits it to an actor, if any.
/// </summary>
public sealed record ProjectileEvent(
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
    /// Grounded: it sits in the game's grounded state - more damage - and is driven along the floor
    /// at its target, as a player's straight sends a grounded structure. Null: the structure's own
    /// default (<see cref="GroundedByDefault"/>).
    /// </summary>
    public bool? Grounded { get; init; }

    /// <summary>The structures the game spawns grounded: pillars, cubes and walls.</summary>
    public static bool GroundedByDefault(string structure) =>
        structure.Equals("Pillar", StringComparison.OrdinalIgnoreCase)
        || structure.Equals("RockCube", StringComparison.OrdinalIgnoreCase)
        || structure.Equals("Wall", StringComparison.OrdinalIgnoreCase);

    public bool IsGrounded => Grounded ?? GroundedByDefault(Structure);
}

/// <summary>An actor performs one of its moves by name (a stack's CachedName, e.g. "Straight").</summary>
public sealed record MoveEvent(double Time, int Order, string ActorId, string Stack) : ChartEvent(Time, Order);

/// <summary>An actor plays a recorded performance (a clip in UserData/BossBattles/Clips) through its pose and stick input.</summary>
public sealed record ClipEvent(double Time, int Order, string ActorId, string Clip) : ChartEvent(Time, Order);

/// <summary>An actor is moved to a new spot and planted there.</summary>
public sealed record PlaceEvent(double Time, int Order, string ActorId, Spot At) : ChartEvent(Time, Order);

/// <summary>Where a projectile is aimed: a point on your end of the lane (x, y at z = 0), or wherever you are.</summary>
public readonly record struct Aim(bool AtPlayer, float X, float Y)
{
    public static Aim Player => new(true, 0f, 0f);

    public static Aim Point(float x, float y) => new(false, x, y);
}
