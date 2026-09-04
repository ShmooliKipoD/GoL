using System;
using System.Collections.Generic;

namespace GoL.Sim.Genetics;

/// <summary>
/// Attributes a lineage does not start with and may acquire by mutation. This is
/// the mechanism the whole project is built around: the set of attributes present
/// in the population grows over generations rather than being fixed at design time.
/// <para>
/// Membership is a bitmask on the genome, so there is room for 64.
/// </para>
/// </summary>
public enum LatentTraitId
{
    /// <summary>Vision reports the hue of what it sees, making body colour a
    /// signalling channel.</summary>
    ColorVision,

    /// <summary>A second, shorter-ranged eye facing backwards.</summary>
    RearEye,

    /// <summary>A second pheromone channel at the nose.</summary>
    SecondNostril,

    /// <summary>Directional smell: which way a scent is getting stronger.</summary>
    ScentGradient,

    /// <summary>Emits pheromone on channel 0.</summary>
    ScentGlandA,

    /// <summary>Emits pheromone on channel 1.</summary>
    ScentGlandB,

    /// <summary>Can eat other creatures, and sense prey in the bite arc.</summary>
    Carnivory,

    /// <summary>Can digest bramble - the plant that colonizes exhausted ground.</summary>
    CelluloseGut,

    /// <summary>Blightcap's toxin barely registers.</summary>
    ToxinResistance,

    /// <summary>A burst of speed at steeply increased cost.</summary>
    SprintGland,

    /// <summary>Two self-connected neurons - a place to hold state across ticks.</summary>
    MemoryCell,

    /// <summary>Halves incoming bite damage, at the cost of some speed.</summary>
    ArmorPlating,

    /// <summary>A rest state: cheap to run, but nearly immobile and half-blind.
    /// <para>
    /// Appended last, and every future trait must be too. Node-id slots are handed
    /// out in catalog build order, so inserting anywhere else renumbers every
    /// existing trait's sensors and effectors.
    /// </para></summary>
    Torpor,
}

/// <summary>What a latent trait costs, what it senses, and what it does.</summary>
public sealed class LatentTrait
{
    public required LatentTraitId Id { get; init; }
    public required string Name { get; init; }

    /// <summary>One line for the attribute overlay.</summary>
    public required string Description { get; init; }

    /// <summary>Must already be unlocked before this one can be. Gives the catalog
    /// a shallow tech tree without any extra machinery.</summary>
    public LatentTraitId? Prerequisite { get; init; }

    /// <summary>Energy per second this trait costs to carry. Non-zero for every
    /// trait: an attribute that is free is never selected against, so the
    /// population would accumulate all of them and none would mean anything.</summary>
    public required float Upkeep { get; init; }

    /// <summary>Trait slot owning this trait's node ids.</summary>
    public required int Slot { get; init; }

    /// <summary>Sensor channels this trait adds.</summary>
    public int SensorChannels { get; init; }

    /// <summary>Effector channels this trait adds.</summary>
    public int EffectorChannels { get; init; }

    /// <summary>Hidden neurons this trait adds, wired with self-loops.</summary>
    public int HiddenNeurons { get; init; }

    public IEnumerable<int> SensorNodes()
    {
        for (int c = 0; c < SensorChannels; c++) yield return NodeIds.Sensor(Slot, c);
    }

    public IEnumerable<int> EffectorNodes()
    {
        for (int c = 0; c < EffectorChannels; c++) yield return NodeIds.Effector(Slot, c);
    }
}

public static class LatentTraitCatalog
{
    public static readonly int Count = Enum.GetValues<LatentTraitId>().Length;

    private static readonly LatentTrait[] Entries = Build();

    public static LatentTrait Get(LatentTraitId id) => Entries[(int)id];

    public static IReadOnlyList<LatentTrait> All => Entries;

    /// <summary>Highest sensor channel index in use, for sizing sense buffers.</summary>
    public static int TotalSensorSlots { get; } = ComputeSensorSlots();

    /// <summary>True if <paramref name="id"/>'s prerequisite is satisfied by the mask.</summary>
    public static bool IsAvailable(LatentTraitId id, ulong unlockedMask)
    {
        var prerequisite = Get(id).Prerequisite;
        return prerequisite is null || (unlockedMask & Bit(prerequisite.Value)) != 0;
    }

    public static ulong Bit(LatentTraitId id) => 1UL << (int)id;

    private static int ComputeSensorSlots()
    {
        int max = NodeIds.FirstLatentSlot + Count;
        return max * NodeIds.SlotStride;
    }

    private static LatentTrait[] Build()
    {
        var entries = new LatentTrait[Count];
        int slot = NodeIds.FirstLatentSlot;

        void Add(LatentTrait trait) => entries[(int)trait.Id] = trait;

        Add(new LatentTrait
        {
            Id = LatentTraitId.ColorVision,
            Name = "Colour vision",
            Description = "Sees the hue of what it looks at",
            Upkeep = 0.010f,
            Slot = slot++,
            SensorChannels = Vision.BinCount,
        });

        Add(new LatentTrait
        {
            Id = LatentTraitId.RearEye,
            Name = "Rear eye",
            Description = "A shorter second eye facing backwards",
            Upkeep = 0.014f,
            Slot = slot++,
            SensorChannels = Vision.RearBinCount * Vision.ChannelsPerBin,
        });

        Add(new LatentTrait
        {
            Id = LatentTraitId.SecondNostril,
            Name = "Second nostril",
            Description = "Smells a second pheromone channel",
            Upkeep = 0.006f,
            Slot = slot++,
            SensorChannels = 1,
        });

        Add(new LatentTrait
        {
            Id = LatentTraitId.ScentGradient,
            Name = "Scent gradient",
            Description = "Senses which way a smell grows stronger",
            Prerequisite = LatentTraitId.SecondNostril,
            Upkeep = 0.012f,
            Slot = slot++,
            SensorChannels = 4,
        });

        Add(new LatentTrait
        {
            Id = LatentTraitId.ScentGlandA,
            Name = "Scent gland",
            Description = "Lays a pheromone trail",
            Upkeep = 0.008f,
            Slot = slot++,
            EffectorChannels = 1,
        });

        Add(new LatentTrait
        {
            Id = LatentTraitId.ScentGlandB,
            Name = "Second gland",
            Description = "Lays a second, distinct pheromone",
            Prerequisite = LatentTraitId.ScentGlandA,
            Upkeep = 0.008f,
            Slot = slot++,
            EffectorChannels = 1,
        });

        Add(new LatentTrait
        {
            Id = LatentTraitId.Carnivory,
            Name = "Carnivory",
            Description = "Eats other creatures, and senses prey it can reach",
            Upkeep = 0.020f,
            Slot = slot++,
            SensorChannels = 1,
            EffectorChannels = 1,
        });

        Add(new LatentTrait
        {
            Id = LatentTraitId.CelluloseGut,
            Name = "Cellulose gut",
            Description = "Digests bramble, which nothing else can eat",
            Upkeep = 0.026f,
            Slot = slot++,
        });

        Add(new LatentTrait
        {
            Id = LatentTraitId.ToxinResistance,
            Name = "Toxin resistance",
            Description = "Shrugs off blightcap poison",
            Upkeep = 0.018f,
            Slot = slot++,
            SensorChannels = 1,
        });

        Add(new LatentTrait
        {
            Id = LatentTraitId.SprintGland,
            Name = "Sprint gland",
            Description = "A burst of speed at steep cost",
            Upkeep = 0.004f,
            Slot = slot++,
            EffectorChannels = 1,
        });

        Add(new LatentTrait
        {
            Id = LatentTraitId.MemoryCell,
            Name = "Memory cell",
            Description = "Neurons that hold state between ticks",
            Upkeep = 0.009f,
            Slot = slot++,
            HiddenNeurons = 2,
        });

        Add(new LatentTrait
        {
            Id = LatentTraitId.ArmorPlating,
            Name = "Armour plating",
            Description = "Halves bite damage, at the cost of speed",
            Upkeep = 0.030f,
            Slot = slot++,
        });

        Add(new LatentTrait
        {
            Id = LatentTraitId.Torpor,
            Name = "Torpor",
            Description = "Rests cheaply, but barely moves and barely sees",
            Upkeep = 0.005f,
            Slot = slot++,
            EffectorChannels = 1,
        });

        return entries;
    }
}

/// <summary>Vision encoding constants, shared by the sensor and its overlay.</summary>
public static class Vision
{
    /// <summary>Angular bins across the forward cone. The eye reports the nearest
    /// hit per bin, which keeps the input vector fixed-size however many things
    /// are in view - and gives occlusion for free.</summary>
    public const int BinCount = 7;

    /// <summary>Per bin: closeness, is-plant, is-creature.</summary>
    public const int ChannelsPerBin = 3;

    public const int RearBinCount = 5;

    /// <summary>Rear eye range as a fraction of the forward eye's.</summary>
    public const float RearRangeFactor = 0.6f;
}
