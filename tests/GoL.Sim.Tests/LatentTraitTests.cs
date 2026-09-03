using GoL.Sim.Brains;
using GoL.Sim.Core;
using GoL.Sim.Genetics;

namespace GoL.Sim.Tests;

/// <summary>
/// Guards the mechanism the project is built around: attributes a lineage did not
/// start with appearing by mutation, and doing so in a way selection can act on.
/// </summary>
public class LatentTraitTests
{
    public static TheoryData<LatentTraitId> AllTraits()
    {
        var data = new TheoryData<LatentTraitId>();
        foreach (var id in Enum.GetValues<LatentTraitId>()) data.Add(id);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllTraits))]
    public void Unlocking_AddsEveryNodeItDeclares_AndWiresThemIn(LatentTraitId id)
    {
        var rng = new Pcg32(1234);
        var genome = Genome.CreateSeed(ref rng);

        // Satisfy the prerequisite chain first, so each trait can be tested alone.
        var prerequisite = LatentTraitCatalog.Get(id).Prerequisite;
        if (prerequisite is not null) Mutator.Unlock(genome, prerequisite.Value, ref rng);

        int sensorsBefore = CountKind(genome, NodeKind.Sensor);
        var trait = LatentTraitCatalog.Get(id);

        Mutator.Unlock(genome, id, ref rng);

        Assert.True(genome.Has(id));

        foreach (int nodeId in trait.SensorNodes())
        {
            Assert.True(genome.HasNode(nodeId), $"{id}: sensor {nodeId} missing");
            // An unwired sensor is invisible to the brain, so the trait would cost
            // upkeep while doing nothing and be selected straight back out.
            Assert.True(genome.HasOutgoing(nodeId), $"{id}: sensor {nodeId} not wired in");
        }

        foreach (int nodeId in trait.EffectorNodes())
        {
            Assert.True(genome.HasNode(nodeId), $"{id}: effector {nodeId} missing");
            Assert.True(genome.HasIncoming(nodeId), $"{id}: effector {nodeId} cannot fire");
        }

        Assert.Equal(sensorsBefore + trait.SensorChannels, CountKind(genome, NodeKind.Sensor));
        Brain.Compile(genome);  // must still compile
    }

    [Fact]
    public void Unlocking_IsIdempotent()
    {
        var rng = new Pcg32(5);
        var genome = Genome.CreateSeed(ref rng);

        Mutator.Unlock(genome, LatentTraitId.Carnivory, ref rng);
        int nodes = genome.Nodes.Count;
        int conns = genome.Conns.Count;

        Mutator.Unlock(genome, LatentTraitId.Carnivory, ref rng);

        Assert.Equal(nodes, genome.Nodes.Count);
        Assert.Equal(conns, genome.Conns.Count);
    }

    [Fact]
    public void TraitWithUnmetPrerequisite_IsNeverOfferedByRandomUnlock()
    {
        var rng = new Pcg32(31337);

        for (int trial = 0; trial < 400; trial++)
        {
            var genome = Genome.CreateSeed(ref rng);
            Mutator.UnlockRandomTrait(genome, ref rng);

            // ScentGradient requires SecondNostril; ScentGlandB requires ScentGlandA.
            if (genome.Has(LatentTraitId.ScentGradient))
                Assert.True(genome.Has(LatentTraitId.SecondNostril));

            if (genome.Has(LatentTraitId.ScentGlandB))
                Assert.True(genome.Has(LatentTraitId.ScentGlandA));
        }
    }

    [Fact]
    public void RandomUnlock_EventuallyReachesEveryTrait()
    {
        var rng = new Pcg32(2024);
        var genome = Genome.CreateSeed(ref rng);

        // Unlock repeatedly on one genome: prerequisites open up as it goes.
        for (int i = 0; i < LatentTraitCatalog.Count * 4; i++)
            Mutator.UnlockRandomTrait(genome, ref rng);

        foreach (var id in Enum.GetValues<LatentTraitId>())
            Assert.True(genome.Has(id), $"{id} was never reachable");

        Assert.False(Mutator.UnlockRandomTrait(genome, ref rng));
    }

    [Fact]
    public void EveryTrait_CostsUpkeep()
    {
        // A free attribute is never selected against, so a population would
        // accumulate all of them and none would signify anything.
        foreach (var trait in LatentTraitCatalog.All)
            Assert.True(trait.Upkeep > 0f, $"{trait.Id} is free to carry");
    }

    [Fact]
    public void EveryTrait_HasADistinctNodeSlot()
    {
        var slots = new HashSet<int>();
        foreach (var trait in LatentTraitCatalog.All)
        {
            Assert.True(slots.Add(trait.Slot), $"{trait.Id} reuses slot {trait.Slot}");
            Assert.True(trait.Slot >= NodeIds.FirstLatentSlot,
                $"{trait.Id} overlaps the innate slots");
        }
    }

    [Fact]
    public void Upkeep_SumsOverUnlockedTraitsOnly()
    {
        var rng = new Pcg32(8);
        var genome = Genome.CreateSeed(ref rng);
        Assert.Equal(0f, genome.LatentUpkeep());

        Mutator.Unlock(genome, LatentTraitId.Carnivory, ref rng);
        Mutator.Unlock(genome, LatentTraitId.ArmorPlating, ref rng);

        float expected = LatentTraitCatalog.Get(LatentTraitId.Carnivory).Upkeep
                       + LatentTraitCatalog.Get(LatentTraitId.ArmorPlating).Upkeep;

        Assert.Equal(expected, genome.LatentUpkeep(), 5);
    }

    private static int CountKind(Genome g, NodeKind kind)
    {
        int n = 0;
        foreach (var node in g.Nodes) if (node.Kind == kind) n++;
        return n;
    }
}
