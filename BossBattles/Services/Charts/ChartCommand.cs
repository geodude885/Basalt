namespace BossBattles.Services.Charts;

/// <summary>
/// What the game side should do, and when. <see cref="ChartRunner"/> turns a chart into these in
/// time order; executing them is all the game side does, so timing lives in tested code.
/// </summary>
public abstract record ChartCommand(double Time);

public sealed record EnterPhase(double Time, Phase Phase) : ChartCommand(Time);

public sealed record PlaceActor(double Time, string ActorId, Spot At) : ChartCommand(Time);

/// <summary>Bring the projectile out and hold it at its origin - the telegraph. <see cref="Id"/> pairs it with its launch.</summary>
public sealed record SpawnProjectile(double Time, int Id, ProjectileEvent Projectile) : ChartCommand(Time);

public sealed record LaunchProjectile(double Time, int Id, ProjectileEvent Projectile) : ChartCommand(Time);

public sealed record RunMove(double Time, string ActorId, string Stack) : ChartCommand(Time);

public sealed record PlayClip(double Time, string ActorId, string Clip) : ChartCommand(Time);

/// <summary>
/// An actor performs a recorded take - its poses, its path in the lane and its moves - from the
/// clip's <see cref="From"/>, until chart time <see cref="Until"/> (null: the clip's end).
/// </summary>
public sealed record PlayTake(double Time, string ActorId, string Clip, double From, double? Until) : ChartCommand(Time)
{
    /// <summary>Moves as recorded, but runs none of the take's moves - see <see cref="TakeSegment.Mime"/>.</summary>
    public bool Mime { get; init; }
}

public sealed record EndChart(double Time) : ChartCommand(Time);
