using System.Globalization;
using GoL.Sim;

namespace GoL.App.Config;

/// <summary>
/// One editable setting on the configuration screen: how to show it and how to
/// step it. Keeping these as a list of descriptors means adding a setting is one
/// entry here rather than an edit in three synchronised places.
/// </summary>
/// <param name="Label">Display name.</param>
/// <param name="Format">Renders the current value.</param>
/// <param name="Step">Returns a new config with the value moved by the given
/// direction (-1 or +1). Clamping belongs here.</param>
public sealed record ConfigField(
    string Label,
    Func<SimConfig, string> Format,
    Func<SimConfig, int, SimConfig> Step);

public static class ConfigFields
{
    private static string Pct(float v) => (v * 100f).ToString("0.##", CultureInfo.InvariantCulture) + "%";
    private static string Num(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static float ClampStep(float value, float delta, float min, float max)
        => Math.Clamp(value + delta, min, max);

    /// <summary>
    /// The settings exposed to the player, in display order. This is a starting
    /// set - Steps 2-4 add the trait and energy tunables as they are implemented.
    /// </summary>
    public static readonly IReadOnlyList<ConfigField> All = new ConfigField[]
    {
        new("Seed",
            c => c.Seed == 0 ? "random" : c.Seed.ToString(CultureInfo.InvariantCulture),
            (c, d) => c with { Seed = Math.Max(0, c.Seed + d) }),

        new("World size",
            c => Num(c.WorldSize),
            (c, d) => c with { WorldSize = ClampStep(c.WorldSize, d * 256f, 512f, 8192f) }),

        new("Wrap edges",
            c => c.Toroidal ? "on" : "off",
            (c, _) => c with { Toroidal = !c.Toroidal }),

        new("Starting creatures",
            c => c.InitialPopulation.ToString(CultureInfo.InvariantCulture),
            (c, d) => c with { InitialPopulation = Math.Clamp(c.InitialPopulation + d * 10, 10, 2000) }),

        new("Plant density",
            c => Pct(c.PlantDensity),
            (c, d) => c with { PlantDensity = ClampStep(c.PlantDensity, d * 0.01f, 0.01f, 0.9f) }),

        new("Base metabolism",
            c => Num(c.BaseMetabolicRate),
            (c, d) => c with { BaseMetabolicRate = ClampStep(c.BaseMetabolicRate, d * 0.05f, 0.05f, 3f) }),

        new("Reproduce at",
            c => Pct(c.ReproductionEnergyFraction),
            (c, d) => c with { ReproductionEnergyFraction = ClampStep(c.ReproductionEnergyFraction, d * 0.05f, 0.2f, 0.95f) }),

        new("Weight mutation",
            c => Pct(c.WeightMutationRate),
            (c, d) => c with { WeightMutationRate = ClampStep(c.WeightMutationRate, d * 0.05f, 0f, 1f) }),

        new("Add connection",
            c => Pct(c.AddConnectionRate),
            (c, d) => c with { AddConnectionRate = ClampStep(c.AddConnectionRate, d * 0.01f, 0f, 1f) }),

        new("Add neuron",
            c => Pct(c.AddNeuronRate),
            (c, d) => c with { AddNeuronRate = ClampStep(c.AddNeuronRate, d * 0.01f, 0f, 1f) }),

        // The headline mechanic: how often a birth grants an attribute the
        // lineage did not previously have. Stepped finely because the useful
        // range is small - see docs/GENOME.md.
        new("New attribute",
            c => (c.TraitUnlockRate * 100f).ToString("0.###", CultureInfo.InvariantCulture) + "%",
            (c, d) => c with { TraitUnlockRate = ClampStep(c.TraitUnlockRate, d * 0.001f, 0f, 0.2f) }),

        new("Population floor",
            c => c.PopulationFloor.ToString(CultureInfo.InvariantCulture),
            (c, d) => c with { PopulationFloor = Math.Clamp(c.PopulationFloor + d * 2, 0, 200) }),
    };
}
