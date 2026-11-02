using System.Globalization;

namespace Basalt.Services.Scoring;

public static class RankLook
{
    private const double Blue = 0.6;
    private const double ShimmerCentre = 0.7;
    private const double ShimmerSpread = 0.1;
    private const double ShimmerPerSecond = 0.5;
    private const double ShimmerPerLetter = 0.25;
    private const double RainbowPerSecond = 0.6;
    private const double RainbowPerLetter = 0.15;
    private static readonly int[] ChannelOffsets = [0, 4, 2];

    public static bool IsAnimated(string rank) => rank is "SS" or "SS+";

    public static string Markup(string rank, double time = 0) => rank switch
    {
        "S" => $"<b>{Letters(rank, _ => Blue)}</b>",
        "SS" => $"<b><size=115%>{Letters(rank, i => ShimmerCentre + (ShimmerSpread * Math.Sin(2 * Math.PI * ((time * ShimmerPerSecond) + (i * ShimmerPerLetter)))))}</size></b>",
        "SS+" => $"<b><size=135%>{Letters(rank, i => (time * RainbowPerSecond) + (i * RainbowPerLetter))}</size></b>",
        _ => rank,
    };

    private static string Letters(string rank, Func<int, double> hue) =>
        string.Concat(rank.Select((letter, i) => $"<color=#{Bright(hue(i))}>{letter}</color>"));

    private static string Bright(double hue)
    {
        var turn = (hue - Math.Floor(hue)) * 6;
        return string.Concat(ChannelOffsets.Select(offset => Channel(turn, offset)));
    }

    private static string Channel(double turn, int offset)
    {
        var level = Math.Clamp(Math.Abs(((turn + offset) % 6) - 3) - 1, 0, 1);
        return ((int)Math.Round(level * 255)).ToString("X2", CultureInfo.InvariantCulture);
    }
}
