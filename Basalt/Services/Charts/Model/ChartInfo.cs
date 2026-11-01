namespace Basalt.Services.Charts.Model;

public record ChartInfo(string? Author, string? Description, string? Difficulty)
{
    internal static ChartInfo From(ChartDto dto) =>
        new(string.IsNullOrWhiteSpace(dto.Author) ? null : dto.Author.Trim(),
            string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
            string.IsNullOrWhiteSpace(dto.Difficulty) ? null : dto.Difficulty.Trim());

    public static ChartInfo None { get; } = new(null, null, null);
}
