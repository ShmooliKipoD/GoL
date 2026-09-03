using GoL.Sim;
using GoL.Sim.Brains;
using GoL.Sim.Core;
using GoL.Sim.Genetics;

namespace GoL.Sim.Tests;

public class GenomeMutationTests
{
    private static Genome Seed(ulong seed = 1)
    {
        var rng = new Pcg32(seed);
        return Genome.CreateSeed(ref rng);
    }

    [Fact]
    public void SeedGenome_HasEveryInnateNode_AndEveryEffectorIsWired()
    {
        var g = Seed();

        foreach (int id in InnateNodeIds.SensorsAndBias())
            Assert.True(g.HasNode(id), $"missing innate node {id}");

        foreach (int id in InnateNodeIds.Effectors())
        {
            Assert.True(g.HasNode(id), $"missing effector {id}");
            // An effector with no input can never fire, so the creature could not
            // act and selection would have nothing to work on.
            Assert.True(g.HasIncoming(id), $"effector {id} has no input");
        }
    }

    /// <summary>
    /// The invariant the whole genome design exists to guarantee. Mutation is
    /// structurally incapable of producing a dangling reference, so this fuzz
    /// should never fail - which is exactly why it is worth running: if it ever
    /// does, an operator has broken the "never remove a node, only connect
    /// existing ones" rule.
    /// </summary>
    [Fact]
    public void Fuzz_ChainedMutations_StayValidAndEvaluable()
    {
        var rng = new Pcg32(20260903);
        var config = new SimConfig
        {
            // Far above real rates: this is a stress test, not a simulation.
            AddConnectionRate = 0.6f,
            AddNeuronRate = 0.4f,
            TraitUnlockRate = 0.25f,
        };

        var genome = Genome.CreateSeed(ref rng);
        var sensors = new float[512];
        var effectors = new float[512];

        for (int step = 0; step < 3000; step++)
        {
            genome = Mutator.Reproduce(genome, ref rng, config);

            AssertStructurallySound(genome, step);

            var brain = Brain.Compile(genome);

            // Drive it with adversarial inputs, and repeatedly, so a recurrent
            // loop has a chance to diverge if activation does not saturate.
            for (int tick = 0; tick < 8; tick++)
            {
                for (int i = 0; i < brain.SensorCount; i++)
                    sensors[i] = rng.NextFloat(-1f, 1f);

                brain.Evaluate(sensors.AsSpan(0, brain.SensorCount),
                               effectors.AsSpan(0, brain.EffectorCount));

                for (int i = 0; i < brain.EffectorCount; i++)
                {
                    Assert.False(float.IsNaN(effectors[i]), $"NaN output at step {step}");
                    Assert.False(float.IsInfinity(effectors[i]), $"infinite output at step {step}");
                    Assert.InRange(effectors[i], -1.001f, 1.001f);
                }
            }
        }

        // A 3000-generation lineage at these rates should have grown substantially.
        Assert.True(genome.Nodes.Count > InnateNodeIds.SensorCount);
        Assert.True(genome.UnlockedMask != 0);
    }

    private static void AssertStructurallySound(Genome g, int step)
    {
        for (int i = 1; i < g.Nodes.Count; i++)
        {
            Assert.True(g.Nodes[i - 1].Id < g.Nodes[i].Id,
                $"nodes out of order at step {step}");
        }

        foreach (var conn in g.Conns)
        {
            Assert.True(g.HasNode(conn.From), $"dangling source {conn.From} at step {step}");
            Assert.True(g.HasNode(conn.To), $"dangling target {conn.To} at step {step}");
            Assert.True(NodeIds.CanReceive(conn.To),
                $"connection writes to a sensor/bias at step {step}");
        }

        // Connections sorted by (To, From): brain evaluation accumulates floats in
        // this order, so it is part of the simulation's semantics, not a detail.
        for (int i = 1; i < g.Conns.Count; i++)
        {
            var a = g.Conns[i - 1];
            var b = g.Conns[i];
            bool ordered = a.To < b.To || (a.To == b.To && a.From < b.From);
            Assert.True(ordered, $"connections out of (To,From) order at step {step}");
        }
    }

    [Fact]
    public void SelfLoopWithLargeWeight_StaysBounded()
    {
        var rng = new Pcg32(7);
        var genome = Genome.CreateSeed(ref rng);

        int hidden = genome.NextHiddenId++;
        genome.InsertNode(new NodeGene(hidden, NodeKind.Hidden, 0.5f));
        genome.AddConnection(hidden, hidden, 50f);
        genome.AddConnection(NodeIds.Bias, hidden, 10f);

        var brain = Brain.Compile(genome);
        var sensors = new float[brain.SensorCount];
        var effectors = new float[brain.EffectorCount];

        // Saturating activation is what keeps a runaway loop finite; without it
        // this diverges to infinity within a few dozen ticks.
        for (int i = 0; i < 10_000; i++)
            brain.Evaluate(sensors, effectors);

        var activations = new float[brain.NodeCount];
        brain.CopyActivations(activations);

        foreach (float a in activations)
        {
            Assert.False(float.IsNaN(a));
            Assert.False(float.IsInfinity(a));
        }
    }

    [Fact]
    public void Reproduce_IsDeterministic_ForTheSameSeed()
    {
        var config = new SimConfig();
        var parent = Seed(99);

        var rngA = new Pcg32(555);
        var rngB = new Pcg32(555);

        var childA = Mutator.Reproduce(parent, ref rngA, config);
        var childB = Mutator.Reproduce(parent, ref rngB, config);

        Assert.Equal(childA.TraitValues, childB.TraitValues);
        Assert.Equal(childA.UnlockedMask, childB.UnlockedMask);
        Assert.Equal(childA.Conns, childB.Conns);
        Assert.Equal(childA.Nodes, childB.Nodes);
    }

    [Fact]
    public void Reproduce_DoesNotMutateTheParent()
    {
        var parent = Seed(3);
        var before = (float[])parent.TraitValues.Clone();
        int nodesBefore = parent.Nodes.Count;
        int connsBefore = parent.Conns.Count;

        var rng = new Pcg32(11);
        var config = new SimConfig { AddConnectionRate = 1f, AddNeuronRate = 1f, TraitUnlockRate = 1f };
        Mutator.Reproduce(parent, ref rng, config);

        Assert.Equal(before, parent.TraitValues);
        Assert.Equal(nodesBefore, parent.Nodes.Count);
        Assert.Equal(connsBefore, parent.Conns.Count);
    }

    [Fact]
    public void Generation_IncrementsWithEachBirth()
    {
        var config = new SimConfig();
        var rng = new Pcg32(4);
        var genome = Genome.CreateSeed(ref rng);

        for (int i = 1; i <= 10; i++)
        {
            genome = Mutator.Reproduce(genome, ref rng, config);
            Assert.Equal(i, genome.Generation);
        }
    }
}
