namespace Basalt.Services.Charts;

/// <summary>What the game side should do, and when; timing lives in <see cref="ChartRunner"/>, which is tested.</summary>
public abstract record ChartCommand(double Time);

public record EnterPhase(double Time, Phase Phase) : ChartCommand(Time);

public record PlaceActor(double Time, string ActorId, Spot At) : ChartCommand(Time);

/// <summary>Bring the projectile out and hold it at its origin - the telegraph. <see cref="Id"/> pairs it with its launch.</summary>
public record SpawnProjectile(double Time, int Id, ProjectileEvent Projectile) : ChartCommand(Time);

public record LaunchProjectile(double Time, int Id, ProjectileEvent Projectile) : ChartCommand(Time);

public record RunMove(double Time, string ActorId, string Stack) : ChartCommand(Time);


/// <summary>An actor performs a clip from clip time <see cref="From"/> to <see cref="To"/>, or its end.</summary>
public record PlayClip(double Time, string ActorId, string Clip, double From, double? To) : ChartCommand(Time)
{
    /// <summary>Moves as recorded, but runs none of the clip's moves - see <see cref="TrackClip.Mime"/>.</summary>
    public bool Mime { get; init; }
}

public record EndChart(double Time) : ChartCommand(Time);
