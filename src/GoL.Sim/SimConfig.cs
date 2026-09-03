namespace GoL.Sim;

/// <summary>
/// Every tunable the simulation reads, in one place. Passed to the world at
/// construction; the sim contains no magic numbers of its own, because these get
/// retuned constantly and a rebuild-per-tweak loop makes balancing impractical.
/// <para>
/// A <b>non-positional</b> record with init-only properties and inline defaults.
/// Both halves of that matter. Non-positional, because this type is persisted to
/// JSON and grows fields every step, so a config written today gets loaded by a
/// later build with fields it has never heard of - a positional record binds
/// through its constructor and every absent member would silently arrive as 0
/// (a zero metabolic rate presents as "evolution does nothing", not as an
/// error), whereas init-only properties keep their initializers. A record,
/// because the configuration screen edits settings with `with` expressions and
/// the world must never see a config mutate under it mid-run.
/// </para>
/// </summary>
public sealed record SimConfig
{
    /// <summary>Seed for the world's RNG streams. A run is reproducible from this
    /// plus the rest of this object; 0 means "pick one at world creation".</summary>
    public int Seed { get; init; }

    /// <summary>Side length of the (square) world in simulation units.</summary>
    public float WorldSize { get; init; } = 2048f;

    /// <summary>Wrap at the edges rather than blocking. Wrapping avoids creatures
    /// evolving degenerate wall-hugging strategies and removes every edge case
    /// from the spatial queries.</summary>
    public bool Toroidal { get; init; } = true;

    /// <summary>Creatures spawned when a run starts.</summary>
    public int InitialPopulation { get; init; } = 120;

    /// <summary>Fraction of plant-grid cells seeded with vegetation at start.</summary>
    public float PlantDensity { get; init; } = 0.18f;

    /// <summary>Simulation steps per second. Fixed - the sim never sees wall-clock
    /// time; the host accumulates real time and calls Tick an integer number of
    /// times, which is what makes a seeded run reproducible.</summary>
    public int TicksPerSecond { get; init; } = 60;

    /// <summary>Energy per second burned by simply existing, before size, brain
    /// complexity and unlocked-trait upkeep are added.</summary>
    public float BaseMetabolicRate { get; init; } = 0.35f;

    /// <summary>Fraction of maximum energy a creature must hold to reproduce.</summary>
    public float ReproductionEnergyFraction { get; init; } = 0.65f;

    /// <summary>Chance per birth of nudging each trait value.</summary>
    public float TraitMutationRate { get; init; } = 1.0f;

    /// <summary>Chance per birth of perturbing connection weights.</summary>
    public float WeightMutationRate { get; init; } = 0.85f;

    /// <summary>Chance per birth of adding a connection to the brain.</summary>
    public float AddConnectionRate { get; init; } = 0.20f;

    /// <summary>Chance per birth of splitting a connection into a new neuron.</summary>
    public float AddNeuronRate { get; init; } = 0.05f;

    /// <summary>
    /// Chance per birth of unlocking a latent trait the lineage did not have.
    /// This is the mutation the whole project is about, and it is deliberately
    /// rare - see docs/GENOME.md.
    /// </summary>
    public float TraitUnlockRate { get; init; } = 0.004f;

    /// <summary>Repopulate from recently successful genomes below this count, so a
    /// mistuned run does not simply end 40 seconds in and teach nothing.</summary>
    public int PopulationFloor { get; init; } = 12;

    public float SecondsPerTick => 1f / TicksPerSecond;
}
