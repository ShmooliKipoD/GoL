using System;

namespace GoL.Sim.Genetics;

/// <summary>
/// The continuous body traits every creature has. Values are stored normalized
/// to 0..1 in the genome and mapped through <see cref="TraitRange"/> on read.
/// <para>
/// An enum indexing a <c>float[]</c> rather than named fields on the creature:
/// mutation perturbs "some trait", and the attribute overlay lists "every trait",
/// and both want to iterate. Named fields would make each of those a switch that
/// has to be extended in lockstep every time a trait is added.
/// </para>
/// </summary>
public enum TraitAxis
{
    /// <summary>Body radius. Bigger creatures hold more energy but cost more to run.</summary>
    BodyRadius,

    /// <summary>Top forward speed.</summary>
    MaxSpeed,

    /// <summary>Turn rate - the spec's "agility", the speed of spin.</summary>
    TurnRate,

    /// <summary>How far the eyes see.</summary>
    EyeRange,

    /// <summary>Half-angle of the vision cone. The full field of view is twice this.</summary>
    EyeHalfFov,

    /// <summary>Radius over which the nose samples pheromones.</summary>
    NoseRadius,

    /// <summary>How far past the body the mouth reaches.</summary>
    MouthReach,

    /// <summary>Half-angle of the bite arc. A creature must face food to eat it.</summary>
    MouthArc,

    /// <summary>Efficiency at digesting the common plant.</summary>
    DigestGrass,

    /// <summary>Efficiency at digesting the scarce high-energy plant.</summary>
    DigestFruit,

    /// <summary>Global metabolic multiplier - the fast-living / slow-living axis.</summary>
    Metabolism,

    /// <summary>Age at which the creature can reproduce, in seconds.</summary>
    MatureAge,

    /// <summary>Fraction of maximum energy needed to reproduce.</summary>
    ReproduceThreshold,

    /// <summary>Cosmetic body hue. Visible to others only once ColorVision is
    /// unlocked, which makes it a signalling channel evolution can exploit.</summary>
    Hue,
}

/// <summary>The real-world span each normalized trait value maps onto.</summary>
/// <param name="Min">Value at a normalized 0.</param>
/// <param name="Max">Value at a normalized 1.</param>
/// <param name="Name">Label for the attribute overlay.</param>
/// <param name="Unit">Suffix for the overlay, or empty.</param>
public readonly record struct TraitRange(float Min, float Max, string Name, string Unit = "")
{
    public float Denormalize(float normalized) => Min + (Max - Min) * normalized;
}

public static class Traits
{
    /// <summary>Number of continuous trait axes.</summary>
    public static readonly int Count = Enum.GetValues<TraitAxis>().Length;

    /// <summary>
    /// Ranges, indexed by <see cref="TraitAxis"/>. Storing traits normalized and
    /// mapping through here means mutation is one clamped Gaussian nudge for every
    /// axis - no per-axis step size to tune - and retuning a range is a data
    /// change that cannot desynchronize from the mutation code.
    /// </summary>
    private static readonly TraitRange[] Ranges = BuildRanges();

    public static TraitRange Range(TraitAxis axis) => Ranges[(int)axis];

    /// <summary>Maps a genome's normalized value for an axis to its real value.</summary>
    public static float Value(float normalized, TraitAxis axis) =>
        Ranges[(int)axis].Denormalize(normalized);

    private static TraitRange[] BuildRanges()
    {
        var r = new TraitRange[Count];

        r[(int)TraitAxis.BodyRadius] = new(3f, 14f, "Size", " u");
        r[(int)TraitAxis.MaxSpeed] = new(10f, 90f, "Speed", " u/s");
        r[(int)TraitAxis.TurnRate] = new(1.0f, 6.0f, "Agility", " rad/s");
        r[(int)TraitAxis.EyeRange] = new(30f, 130f, "Eye range", " u");
        r[(int)TraitAxis.EyeHalfFov] = new(0.15f, 1.30f, "Eye FOV", " rad");
        r[(int)TraitAxis.NoseRadius] = new(0f, 120f, "Nose radius", " u");
        r[(int)TraitAxis.MouthReach] = new(0.5f, 5f, "Mouth reach", " u");
        r[(int)TraitAxis.MouthArc] = new(0.2f, 1.2f, "Mouth arc", " rad");
        r[(int)TraitAxis.DigestGrass] = new(0.3f, 1.0f, "Digest grass");
        r[(int)TraitAxis.DigestFruit] = new(0.3f, 1.0f, "Digest fruit");
        r[(int)TraitAxis.Metabolism] = new(0.7f, 1.4f, "Metabolism", "x");
        r[(int)TraitAxis.MatureAge] = new(20f, 120f, "Mature at", " s");
        r[(int)TraitAxis.ReproduceThreshold] = new(0.45f, 0.9f, "Breed at");
        r[(int)TraitAxis.Hue] = new(0f, 1f, "Hue");

        return r;
    }
}
