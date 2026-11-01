using Basalt.Services;
using Xunit;

namespace Basalt.Tests.Services;

public sealed class JsonBookTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"Basalt-{Guid.NewGuid():N}", "places.json");

    [Fact]
    public void ASpotSurvivesARestartAndABrokenFileStartsEmpty()
    {
        var book = new JsonBook<SavedSpot>(_file);
        book.Load();
        Assert.Null(book.Get("Gym"));

        book.Set("Gym", new SavedSpot(1f, 2f, 3f, 90f));
        var again = new JsonBook<SavedSpot>(_file);
        again.Load();
        Assert.Equal(new SavedSpot(1f, 2f, 3f, 90f), again.Get("Gym"));

        File.WriteAllText(_file, "{ broken");
        again.Load();
        Assert.Null(again.Get("Gym"));
    }

    public void Dispose()
    {
        var folder = Path.GetDirectoryName(_file);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }
}
