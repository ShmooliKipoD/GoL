using System;
using GoL.Sim.Genetics;

namespace GoL.Sim.Board;

/// <summary>
/// The kinds of green the board grows. Ordering matters only in that
/// <see cref="None"/> is 0, so a cleared grid reads as empty.
/// </summary>
public enum PlantKind : byte
{
    None = 0,

    /// <summary>The staple. Everywhere, cheap, low value.</summary>
    Grass,

    /// <summary>Scarce and rich, and only on good ground - worth crossing the map for.</summary>
    Fruit,

    /// <summary>
    /// The locked niche. Thrives on exhausted ground that nothing else will
    /// colonise, and is <b>edible only with a CelluloseGut</b>. A population that
    /// never unlocks that attribute hits a hard ceiling while bramble takes the map.
    /// </summary>
    Bramble,

    /// <summary>High energy and poisonous, unless the eater has ToxinResistance.
    /// Gives that attribute something to be worth having.</summary>
    Blightcap,

    /// <summary>A corpse. Free food for anything, but properly digestible only with
    /// Carnivory - so carnivory pays off before a lineage can reliably hunt.</summary>
    Carrion,
}

/// <summary>How one kind of plant behaves. Data, so balancing is an edit here.</summary>
public sealed record PlantSpec(
    PlantKind Kind,
    float MaxEnergy,
    float GrowthRate,
    float MaturityRate,
    float SpreadChance,
    float MinFertility,
    float MaxFertility,
    float FertilityDrain,
    float Radius);

public static class PlantSpecs
{
    private static readonly PlantSpec[] Specs = Build();

    public static PlantSpec Get(PlantKind kind) => Specs[(int)kind];

    /// <summary>
    /// The kinds that can appear on their own. Gated on <c>SpreadChance &gt; 0</c>,
    /// which excludes carrion - it neither grows nor spreads and is planted only by
    /// a death, so seeding it would conjure corpses from nothing.
    /// </summary>
    public static readonly PlantSpec[] Seedable =
        Array.FindAll(Specs, s => s is not null && s.SpreadChance > 0f);

    /// <summary>
    /// Energy actually gained from a bite, after the eater's digestion.
    /// <para>
    /// This is where the latent attributes cash out: bramble is worth nothing
    /// without a cellulose gut, blightcap poisons anything without resistance, and
    /// carrion is barely worth eating without carnivory.
    /// </para>
    /// </summary>
    public static float Digestibility(PlantKind kind, Genome genome) => kind switch
    {
        PlantKind.Grass => genome.Trait(TraitAxis.DigestGrass),
        PlantKind.Fruit => genome.Trait(TraitAxis.DigestFruit),
        PlantKind.Bramble => genome.Has(LatentTraitId.CelluloseGut) ? 0.6f : 0f,
        PlantKind.Blightcap => 0.9f,
        PlantKind.Carrion => genome.Has(LatentTraitId.Carnivory) ? 0.75f : 0.15f,
        _ => 0f,
    };

    /// <summary>Toxin taken on per bite.</summary>
    public static float Toxicity(PlantKind kind, Genome genome)
    {
        if (kind != PlantKind.Blightcap) return 0f;
        return genome.Has(LatentTraitId.ToxinResistance) ? 9f * 0.15f : 9f;
    }

    private static PlantSpec[] Build()
    {
        var specs = new PlantSpec[Enum.GetValues<PlantKind>().Length];

        specs[(int)PlantKind.None] =
            new(PlantKind.None, 0f, 0f, 0f, 0f, 0f, 1f, 0f, 0f);

        // Common, fast, spreads readily onto anything but barren ground.
        specs[(int)PlantKind.Grass] =
            new(PlantKind.Grass, 12f, 1.2f, 0.5f, 0.010f, 0.15f, 1f, 0.020f, 3.0f);

        // Rare and rich. Needs good soil and drains it hard, so fruit patches move.
        specs[(int)PlantKind.Fruit] =
            new(PlantKind.Fruit, 45f, 0.35f, 0.2f, 0.008f, 0.55f, 1f, 0.100f, 4.5f);

        // Colonises land the others have exhausted - which is what makes the
        // cellulose gut worth its upkeep.
        specs[(int)PlantKind.Bramble] =
            new(PlantKind.Bramble, 26f, 0.7f, 0.4f, 0.010f, 0.05f, 1f, 0.015f, 3.5f);

        // Only on poor ground, so it is the reward for going where food is scarce.
        specs[(int)PlantKind.Blightcap] =
            new(PlantKind.Blightcap, 60f, 0.5f, 0.3f, 0.020f, 0f, 0.25f, 0.020f, 4.0f);

        // Does not grow or spread; decays.
        specs[(int)PlantKind.Carrion] =
            new(PlantKind.Carrion, 80f, 0f, 1f, 0f, 0f, 1f, 0f, 4.0f);

        return specs;
    }
}
