namespace Basalt.Services.Charts;

public abstract record ChartCommand(double Time);

public record EnterPhase(double Time, Phase Phase) : ChartCommand(Time);

public record PlaceActor(double Time, string ActorId, Spot At) : ChartCommand(Time);

// Spawns but does not launch
public record SpawnProjectile(double Time, int Id, ProjectileEvent Projectile) : ChartCommand(Time);

public record LaunchProjectile(double Time, int Id, ProjectileEvent Projectile) : ChartCommand(Time);

public record RunMove(double Time, string ActorId, string Stack) : ChartCommand(Time);

public record PlayClip(double Time, string ActorId, string Clip, double From, double? To) : ChartCommand(Time)
{
    public bool Mime { get; init; }
}

public record EndChart(double Time) : ChartCommand(Time);
