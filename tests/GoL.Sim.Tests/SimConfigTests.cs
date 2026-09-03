using System.Text.Json;
using GoL.Sim;

namespace GoL.Sim.Tests;

public class SimConfigTests
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    [Fact]
    public void RoundTrips_ThroughJson_Unchanged()
    {
        var original = new SimConfig
        {
            Seed = 4242,
            WorldSize = 4096f,
            Toroidal = false,
            InitialPopulation = 300,
            TraitUnlockRate = 0.02f,
        };

        var restored = JsonSerializer.Deserialize<SimConfig>(
            JsonSerializer.Serialize(original, Options), Options);

        Assert.Equal(original, restored);
    }

    /// <summary>
    /// The hazard this type is shaped to avoid. A config file written by an earlier
    /// build has no entry for fields added later; those must fall back to their
    /// property initializers, not to 0. A zero metabolic rate would present as
    /// "evolution mysteriously does nothing" rather than as an error, which is why
    /// SimConfig is a NON-POSITIONAL record - a positional one binds through the
    /// constructor and would silently zero every absent member.
    /// </summary>
    [Fact]
    public void MissingFields_KeepTheirDefaults_RatherThanBecomingZero()
    {
        // A deliberately sparse file, as an older build would have written it.
        const string legacyJson = """
        {
          "Seed": 7,
          "WorldSize": 1024
        }
        """;

        var loaded = JsonSerializer.Deserialize<SimConfig>(legacyJson, Options)!;
        var defaults = new SimConfig();

        Assert.Equal(7, loaded.Seed);
        Assert.Equal(1024f, loaded.WorldSize);

        Assert.Equal(defaults.BaseMetabolicRate, loaded.BaseMetabolicRate);
        Assert.Equal(defaults.TraitUnlockRate, loaded.TraitUnlockRate);
        Assert.Equal(defaults.InitialPopulation, loaded.InitialPopulation);
        Assert.Equal(defaults.TicksPerSecond, loaded.TicksPerSecond);
        Assert.True(loaded.Toroidal);

        Assert.NotEqual(0f, loaded.BaseMetabolicRate);
    }

    [Fact]
    public void Defaults_AreUsable_NotZero()
    {
        var c = new SimConfig();

        Assert.True(c.WorldSize > 0f);
        Assert.True(c.InitialPopulation > 0);
        Assert.True(c.TicksPerSecond > 0);
        Assert.True(c.BaseMetabolicRate > 0f);
        Assert.Equal(1f / c.TicksPerSecond, c.SecondsPerTick, 6);
    }

    [Fact]
    public void WithExpression_ChangesOneFieldAndLeavesTheRest()
    {
        var a = new SimConfig();
        var b = a with { Seed = 99 };

        Assert.Equal(99, b.Seed);
        Assert.Equal(a.WorldSize, b.WorldSize);
        Assert.Equal(a.TraitUnlockRate, b.TraitUnlockRate);
        Assert.NotEqual(a, b);
    }
}
