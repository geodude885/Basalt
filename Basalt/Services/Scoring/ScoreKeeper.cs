namespace Basalt.Services.Scoring;

public record BattleResult(
    string Chart,
    bool Survived,
    double Seconds,
    int DamageTaken,
    int HitsTaken,
    double ActorDamage,
    int ProjectilesLaunched,
    int Score,
    string Rank);

public record BestScore(int Score, string Rank, bool Survived, DateTime When)
{
    public static BestScore From(BattleResult result, DateTime when) => new(result.Score, result.Rank, result.Survived, when);

    public bool IsBeatenBy(BattleResult result) => result.Score > Score;
}

// Surviving to the end: 1000, otherwise * 0.9 * percentage lasted
// Damage to actors adds points
// Damage taken removes points
// Rank is by damage taken; damage to actors can offset it, up to an A
public class ScoreKeeper
{
    public const int SurvivalPoints = 1000;
    public const int ActorDamagePoints = 10;
    public const int DamageTakenPoints = 25;

    public const string Hitless = "SS+";

    public static readonly (string Rank, int MaxDamage)[] Ranks =
    {
        ("SS", 2),
        ("S", 4),
        ("A+", 6),
        ("A", 10),
        ("B+", 13),
        ("B", 16),
        ("C+", 18),
        ("C", int.MaxValue),
    };

    private const double CompletionRequiredForDPlus = 0.5;
    private const string DealingDamageCanRaiseToRank = "A";
    private const int MaximumDamageCountedAgainstScore = 20;

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

        var survival = survived ? SurvivalPoints : SurvivalPoints * 0.9 * lasted;

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
            Rank(survived, DamageTaken, _actorDamage, lasted));
    }

    public static string Rank(bool survived, int damageTaken, double actorDamage = 0, double lasted = 1)
    {
        if (survived && damageTaken == 0) return Hitless;

        var taken = survived ? damageTaken : Math.Max(damageTaken, MaximumDamageCountedAgainstScore);

        var boosted = Math.Max(
            Position(taken - (actorDamage * ActorDamagePoints / DamageTakenPoints)),
            Array.FindIndex(Ranks, r => r.Rank == DealingDamageCanRaiseToRank));

        if (survived) return Ranks[Math.Min(Position(taken), boosted)].Rank;

        if (boosted < Ranks.Length - 1) return Ranks[boosted].Rank;
        return lasted > CompletionRequiredForDPlus ? "D+" : "D";
    }

    private static int Position(double damage) => Array.FindIndex(Ranks, r => damage <= r.MaxDamage);
}
