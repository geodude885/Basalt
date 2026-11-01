using System.Text.Json;
using Basalt.Services.Charts.Model;

namespace Basalt.Services.Charts.Files;

public record ChartLoadResult(Chart? Chart, IReadOnlyList<string> Errors)
{
    public bool Ok => Chart != null;
}

public static class ChartLoader
{
    public const int NewestFormat = 3;

    public static ChartLoadResult Load(string json)
    {
        ChartDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ChartDto>(json, ChartJson.JsonOptions);
        }
        catch (JsonException e)
        {
            return new ChartLoadResult(null, [$"not valid chart JSON: {e.Message}"]);
        }

        if (dto == null) return new ChartLoadResult(null, ["the file is empty"]);

        if (dto.Format is > NewestFormat)
            return new ChartLoadResult(null, [$"made for a newer Basalt (chart format {dto.Format}; this one reads up to {NewestFormat}) - update the mod"]);

        var errors = new ChartValidator(dto).Validate().ToList();

        return errors.Count == 0 ?
            new ChartLoadResult(Chart.From(dto), errors)
            : new ChartLoadResult(null, errors);
    }
}
