using System;
using System.Collections.Generic;
using GoL.Sim.Core;

namespace GoL.Sim.Genetics;

/// <summary>How strongly each mutation operator perturbs. Separate from
/// <see cref="SimConfig"/>'s rates, which say how <i>often</i> they fire.</summary>
public sealed record MutationStrength
{
    /// <summary>Standard deviation of a trait nudge, in normalized units.</summary>
    public float TraitSigma { get; init; } = 0.04f;

    /// <summary>Standard deviation of a weight nudge.</summary>
    public float WeightSigma { get; init; } = 0.12f;

    /// <summary>Chance a weight is replaced outright rather than nudged. Nudges
    /// alone can only refine a weight; replacement is how a lineage escapes a
    /// weight that is simply the wrong sign.</summary>
    public float WeightReplaceChance { get; init; } = 0.10f;

    public float BiasSigma { get; init; } = 0.08f;

    /// <summary>Chance of flipping one connection's enabled flag.</summary>
    public float ToggleConnectionRate { get; init; } = 0.02f;
}

/// <summary>
/// Produces a mutated child genome from a parent. Asexual: there is no crossover.
/// <para>
/// Crossover in a variable-topology network needs gene alignment, disjoint/excess
/// handling and usually speciation to be worth anything, and delivers little until
/// the population is large and stable. It can be added later without changing the
/// genome format.
/// </para>
/// </summary>
public static class Mutator
{
    /// <summary>
    /// Clones the parent and applies mutations. The operator order is fixed, and
    /// each step draws a known number of values from <paramref name="rng"/> - that
    /// is what makes a lineage reproducible from its seed.
    /// </summary>
    public static Genome Reproduce(
        Genome parent, ref Pcg32 rng, SimConfig config, MutationStrength? strength = null)
    {
        var s = strength ?? new MutationStrength();
        var child = parent.Clone();
        child.Generation = parent.Generation + 1;

        MutateTraits(child, ref rng, config, s);
        MutateWeights(child, ref rng, config, s);
        MutateBiases(child, ref rng, s);
        ToggleConnection(child, ref rng, s);

        if (rng.Chance(config.AddConnectionRate)) AddConnection(child, ref rng);
        if (rng.Chance(config.AddNeuronRate)) AddNeuron(child, ref rng);
        if (rng.Chance(config.TraitUnlockRate)) UnlockRandomTrait(child, ref rng);

        return child;
    }

    private static void MutateTraits(
        Genome g, ref Pcg32 rng, SimConfig config, MutationStrength s)
    {
        if (!rng.Chance(config.TraitMutationRate)) return;

        for (int i = 0; i < g.TraitValues.Length; i++)
        {
            g.TraitValues[i] = Math.Clamp(
                g.TraitValues[i] + rng.NextGauss(0f, s.TraitSigma), 0f, 1f);
        }
    }

    private static void MutateWeights(
        Genome g, ref Pcg32 rng, SimConfig config, MutationStrength s)
    {
        if (!rng.Chance(config.WeightMutationRate)) return;

        for (int i = 0; i < g.Conns.Count; i++)
        {
            var c = g.Conns[i];
            c.Weight = rng.Chance(s.WeightReplaceChance)
                ? rng.NextGauss(0f, 1f)
                : c.Weight + rng.NextGauss(0f, s.WeightSigma);
            g.Conns[i] = c;
        }
    }

    private static void MutateBiases(Genome g, ref Pcg32 rng, MutationStrength s)
    {
        for (int i = 0; i < g.Nodes.Count; i++)
        {
            var n = g.Nodes[i];
            if (n.Kind is NodeKind.Sensor or NodeKind.Bias) continue;
            g.Nodes[i] = n with { Bias = n.Bias + rng.NextGauss(0f, s.BiasSigma) };
        }
    }

    private static void ToggleConnection(Genome g, ref Pcg32 rng, MutationStrength s)
    {
        if (g.Conns.Count == 0) return;
        if (!rng.Chance(s.ToggleConnectionRate)) return;

        int i = rng.NextInt(g.Conns.Count);
        var c = g.Conns[i];
        g.Conns[i] = c with { Enabled = !c.Enabled };
    }

    /// <summary>Connects two existing nodes. Endpoints are sampled from the node
    /// list, so the result cannot dangle.</summary>
    private static void AddConnection(Genome g, ref Pcg32 rng)
    {
        // A handful of attempts: most failures are duplicate pairs, and retrying
        // forever on a saturated small brain would hang the tick.
        for (int attempt = 0; attempt < 8; attempt++)
        {
            int from = g.Nodes[rng.NextInt(g.Nodes.Count)].Id;
            int to = g.Nodes[rng.NextInt(g.Nodes.Count)].Id;

            if (!NodeIds.CanReceive(to)) continue;
            if (g.AddConnection(from, to, rng.NextGauss(0f, 0.8f))) return;
        }
    }

    /// <summary>
    /// Splits an enabled connection a-&gt;b into a-&gt;h-&gt;b. The incoming weight is 1
    /// and the outgoing weight is the old one, so the new neuron starts out very
    /// nearly behaviourally neutral - a structural change that does not immediately
    /// destroy whatever the lineage had already learned.
    /// </summary>
    private static void AddNeuron(Genome g, ref Pcg32 rng)
    {
        int enabled = g.EnabledConnCount();
        if (enabled == 0) return;

        int target = rng.NextInt(enabled);
        int index = -1;
        for (int i = 0, seen = 0; i < g.Conns.Count; i++)
        {
            if (!g.Conns[i].Enabled) continue;
            if (seen++ == target) { index = i; break; }
        }
        if (index < 0) return;

        var old = g.Conns[index];
        g.Conns[index] = old with { Enabled = false };

        int hiddenId = g.NextHiddenId++;
        g.InsertNode(new NodeGene(hiddenId, NodeKind.Hidden, 0f));

        g.AddConnection(old.From, hiddenId, 1f);
        g.AddConnection(hiddenId, old.To, old.Weight);
    }

    /// <summary>
    /// Grants an attribute the lineage did not have, inserting the sensor,
    /// effector and hidden nodes it declares.
    /// <para>
    /// <b>The new nodes are wired immediately</b> - every new sensor gets at least
    /// one outgoing connection and every new effector at least one incoming. An
    /// unwired sensor is invisible to the brain and an unwired effector can never
    /// fire, so the unlock would cost upkeep while doing precisely nothing, and
    /// selection would remove it before it ever had a chance to be useful.
    /// </para>
    /// </summary>
    public static bool UnlockRandomTrait(Genome g, ref Pcg32 rng)
    {
        Span<int> available = stackalloc int[LatentTraitCatalog.Count];
        int count = 0;

        for (int i = 0; i < LatentTraitCatalog.Count; i++)
        {
            var id = (LatentTraitId)i;
            if (g.Has(id)) continue;
            if (!LatentTraitCatalog.IsAvailable(id, g.UnlockedMask)) continue;
            available[count++] = i;
        }

        if (count == 0) return false;

        Unlock(g, (LatentTraitId)available[rng.NextInt(count)], ref rng);
        return true;
    }

    /// <summary>Grants a specific latent trait. Idempotent.</summary>
    public static void Unlock(Genome g, LatentTraitId id, ref Pcg32 rng)
    {
        if (g.Has(id)) return;

        var trait = LatentTraitCatalog.Get(id);
        g.UnlockedMask |= LatentTraitCatalog.Bit(id);

        var newSensors = new List<int>();
        foreach (int nodeId in trait.SensorNodes())
        {
            g.InsertNode(new NodeGene(nodeId, NodeKind.Sensor, 0f));
            newSensors.Add(nodeId);
        }

        var newEffectors = new List<int>();
        foreach (int nodeId in trait.EffectorNodes())
        {
            g.InsertNode(new NodeGene(nodeId, NodeKind.Effector, 0f));
            newEffectors.Add(nodeId);
        }

        // Memory neurons come with self-loops: that is what makes them memory.
        var newHidden = new List<int>();
        for (int i = 0; i < trait.HiddenNeurons; i++)
        {
            int hiddenId = g.NextHiddenId++;
            g.InsertNode(new NodeGene(hiddenId, NodeKind.Hidden, 0f));
            g.AddConnection(hiddenId, hiddenId, rng.NextFloat(0.75f, 0.95f));
            newHidden.Add(hiddenId);
        }

        foreach (int sensor in newSensors)
            g.AddConnection(sensor, PickReceiver(g, ref rng), rng.NextGauss(0f, 0.8f));

        foreach (int effector in newEffectors)
            g.AddConnection(PickSource(g, ref rng), effector, rng.NextGauss(0f, 0.8f));

        foreach (int hidden in newHidden)
        {
            g.AddConnection(PickSource(g, ref rng), hidden, rng.NextGauss(0f, 0.8f));
            g.AddConnection(hidden, PickReceiver(g, ref rng), rng.NextGauss(0f, 0.8f));
        }
    }

    /// <summary>A node that may be written to - anything but a sensor or the bias.</summary>
    private static int PickReceiver(Genome g, ref Pcg32 rng)
    {
        for (int attempt = 0; attempt < 16; attempt++)
        {
            int id = g.Nodes[rng.NextInt(g.Nodes.Count)].Id;
            if (NodeIds.CanReceive(id)) return id;
        }

        // Every genome has innate effectors, so this always finds one.
        for (int i = 0; i < g.Nodes.Count; i++)
            if (NodeIds.CanReceive(g.Nodes[i].Id)) return g.Nodes[i].Id;

        return NodeIds.Effector(NodeIds.InnateSlot, 0);
    }

    /// <summary>Any node may be a source, including effectors - reading an
    /// effector's own last output is a cheap and legitimate feedback path.</summary>
    private static int PickSource(Genome g, ref Pcg32 rng) =>
        g.Nodes[rng.NextInt(g.Nodes.Count)].Id;
}
