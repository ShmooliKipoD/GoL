using GoL.Sim;

namespace GoL.Sim.Tests;

public class SimConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "gol-config-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Load_WithNoFile_ReturnsDefaults()
    {
        var store = new SimConfigStore(_dir);
        Assert.Equal(new SimConfig(), store.Load());
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var store = new SimConfigStore(_dir);
        var config = new SimConfig { Seed = 1234, InitialPopulation = 500, Toroidal = false };

        Assert.True(store.Save(config));
        Assert.True(File.Exists(store.FilePath));
        Assert.Equal(config, store.Load());
    }

    [Fact]
    public void Save_CreatesTheDirectoryIfMissing()
    {
        var nested = Path.Combine(_dir, "deeper");
        Assert.True(new SimConfigStore(nested).Save(new SimConfig()));
        Assert.True(Directory.Exists(nested));
    }

    /// <summary>
    /// Persistence is best-effort by design: a settings file someone hand-edited
    /// into invalid JSON must not stop the game launching.
    /// </summary>
    [Fact]
    public void Load_WithCorruptFile_FallsBackToDefaults_WithoutThrowing()
    {
        Directory.CreateDirectory(_dir);
        var store = new SimConfigStore(_dir);
        File.WriteAllText(store.FilePath, "{ this is not json ");

        Assert.Equal(new SimConfig(), store.Load());
    }

    [Fact]
    public void Load_WithFileFromAnEarlierBuild_KeepsDefaultsForUnknownFields()
    {
        Directory.CreateDirectory(_dir);
        var store = new SimConfigStore(_dir);
        File.WriteAllText(store.FilePath, """{ "Seed": 9, "InitialPopulation": 42 }""");

        var loaded = store.Load();
        var defaults = new SimConfig();

        Assert.Equal(9, loaded.Seed);
        Assert.Equal(42, loaded.InitialPopulation);
        Assert.Equal(defaults.BaseMetabolicRate, loaded.BaseMetabolicRate);
        Assert.Equal(defaults.TraitUnlockRate, loaded.TraitUnlockRate);
    }
}
