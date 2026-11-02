using Basalt.Services;
using Xunit;

namespace Basalt.Tests.Services;

public sealed class JsonFileRepositoryTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"Basalt-{Guid.NewGuid():N}", "places.json");

    [Fact]
    public void ASpotSurvivesARestartAndABrokenFileStartsEmpty()
    {
        var store = new JsonFileRepository<SavedSpot>(_file);
        store.Load();
        Assert.Null(store.Get("Gym"));

        store.Set("Gym", new SavedSpot(1f, 2f, 3f, 90f));
        var again = new JsonFileRepository<SavedSpot>(_file);
        again.Load();
        Assert.Equal(new SavedSpot(1f, 2f, 3f, 90f), again.Get("Gym"));

        File.WriteAllText(_file, "{ broken");
        again.Load();
        Assert.Null(again.Get("Gym"));
    }

    [Fact]
    public void ABrokenFileIsKeptAsideNotOverwritten()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, "{ broken");

        var store = new JsonFileRepository<SavedSpot>(_file);
        store.Load();
        store.Set("Gym", new SavedSpot(1f, 2f, 3f, 90f));

        Assert.Equal("{ broken", File.ReadAllText(_file + ".bad"));
        var again = new JsonFileRepository<SavedSpot>(_file);
        again.Load();
        Assert.Null(again.LoadError);
        Assert.Equal(new SavedSpot(1f, 2f, 3f, 90f), again.Get("Gym"));
    }

    public void Dispose()
    {
        var folder = Path.GetDirectoryName(_file);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }
}
