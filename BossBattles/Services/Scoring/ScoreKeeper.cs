namespace BossBattles.Services.Scoring;

/// <summary>How a battle went. <see cref="Rank"/> is S, A, B, C, or D for not surviving.</summary>
public sealed record BattleResult(
    string Chart,
    bool Survived,
    double Seconds,
    int DamageTaken,
    int HitsTaken,
    double ActorDamage,
    int ProjectilesLaunched,
    int Score,
    string Rank);

/// <summary>
/// Scores a battle. The goal is to survive; hurting actors is the bonus:
/// <list type="bullet">
/// <item>Surviving to the end: <see cref="SurvivalPoints"/>. Falling short: a share of half of it,
/// for the fraction of the battle lasted.</item>
/// <item>Each point of damage on an actor: <see cref="ActorDamagePoints"/>, times the phase's
/// score multiplier.</item>
/// <item>Each point of damage you clip: minus <see cref="DamageTakenPoints"/>.</item>
/// </list>
/// Rank is about staying clean: S untouched, A up to 3 damage, B up to 8, C any survival, D not.
/// </summary>
public sealed class ScoreKeeper
{
    public const int SurvivalPoints = 1000;
    public const int ActorDamagePoints = 10;
    public const int DamageTakenPoints = 25;

    private readonly string _chart;
    private int _hitsTaken;
    private double _actorDamage;
    private int _launched;

    public ScoreKeeper(string chart)
    {
        _chart = chart;
    }

    public int DamageTaken { get; private set; }

    public void PlayerDamaged(int amount)
    {
        if (amount <= 0) return;
        DamageTaken += amount;
        _hitsTaken++;
    }

    public void ActorDamaged(int amount, double multiplier)
    {
        if (amount > 0) _actorDamage += amount * multiplier;
    }

    public void ProjectileLaunched() => _launched++;

    public BattleResult Finish(bool survived, double seconds, double battleLength)
    {
        var lasted = battleLength <= 0 ? 1 : Math.Clamp(seconds / battleLength, 0, 1);
        var survival = survived ? SurvivalPoints : SurvivalPoints * 0.5 * lasted;
        var score = survival + (_actorDamage * ActorDamagePoints) - (DamageTaken * DamageTakenPoints);

        return new BattleResult(
            _chart,
            survived,
            seconds,
            DamageTaken,
            _hitsTaken,
            _actorDamage,
            _launched,
            (int)Math.Max(0, Math.Round(score)),
            Rank(survived, DamageTaken));
    }

    public static string Rank(bool survived, int damageTaken) =>
        !survived ? "D"
        : damageTaken == 0 ? "S"
        : damageTaken <= 3 ? "A"
        : damageTaken <= 8 ? "B"
        : "C";
}
