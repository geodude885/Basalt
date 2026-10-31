namespace Basalt.Services.Charts.Model;

public record ChartInfo(string? Author, string? Description, string? Difficulty)
{
    public static ChartInfo None { get; } = new(null, null, null);
}
