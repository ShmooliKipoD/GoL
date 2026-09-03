using System;
using System.Collections.Generic;

namespace GoL.Sim.Genetics;

public enum NodeKind : byte { Bias = 0, Sensor = 1, Hidden = 2, Effector = 3 }

/// <summary>One neuron.</summary>
public readonly record struct NodeGene(int Id, NodeKind Kind, float Bias);

/// <summary>One weighted connection.</summary>
public record struct ConnGene(int From, int To, float Weight, bool Enabled);

/// <summary>
/// A creature's heritable design: its body traits, which latent attributes it has
/// acquired, and the topology and weights of its brain.
/// <para>
/// <b>Evaluability is structural, not repaired after the fact.</b> No mutation
/// ever removes a node; connections are only ever created between nodes that
/// already exist; and adding a neuron splits an existing connection. A dangling
/// reference is therefore unconstructible, so there is no validation pass that
/// could itself be buggy. Cycles need no handling either - see <c>Brain</c>.
/// </para>
/// </summary>
public sealed class Genome
{
    /// <summary>Normalized 0..1 per <see cref="TraitAxis"/>. An array, not a
    /// dictionary: mutation and the attribute overlay both iterate these, and hash
    /// iteration order would break run reproducibility.</summary>
    public float[] TraitValues { get; }

    /// <summary>One bit per <see cref="LatentTraitId"/>. Membership only grows.</summary>
    public ulong UnlockedMask { get; set; }

    /// <summary>Sorted ascending by <see cref="NodeGene.Id"/>.</summary>
    public List<NodeGene> Nodes { get; }

    /// <summary>Sorted by (To, From) - a key derived purely from node ids, never
    /// from insertion order. Brain evaluation accumulates floats in this order and
    /// float addition is not associative, so the order is part of the simulation's
    /// semantics: two genomes that reached the same topology by different mutation
    /// paths must evaluate identically.</summary>
    public List<ConnGene> Conns { get; }

    public int NextHiddenId { get; set; } = NodeIds.HiddenBase;

    public int Generation { get; set; }

    private Genome(float[] traits, List<NodeGene> nodes, List<ConnGene> conns)
    {
        TraitValues = traits;
        Nodes = nodes;
        Conns = conns;
    }

    public bool Has(LatentTraitId id) => (UnlockedMask & LatentTraitCatalog.Bit(id)) != 0;

    /// <summary>Real (denormalized) value of a body trait.</summary>
    public float Trait(TraitAxis axis) => Traits.Value(TraitValues[(int)axis], axis);

    /// <summary>Normalized 0..1 value, for mutation and for the overlay's bars.</summary>
    public float Normalized(TraitAxis axis) => TraitValues[(int)axis];

    /// <summary>Total upkeep of every unlocked latent trait, in energy per second.</summary>
    public float LatentUpkeep()
    {
        float total = 0f;
        for (int i = 0; i < LatentTraitCatalog.Count; i++)
        {
            if ((UnlockedMask & (1UL << i)) != 0)
                total += LatentTraitCatalog.All[i].Upkeep;
        }
        return total;
    }

    public Genome Clone()
    {
        var clone = new Genome(
            (float[])TraitValues.Clone(),
            new List<NodeGene>(Nodes),
            new List<ConnGene>(Conns))
        {
            UnlockedMask = UnlockedMask,
            NextHiddenId = NextHiddenId,
            Generation = Generation,
        };
        return clone;
    }

    /// <summary>
    /// The ancestral genome: every innate sensor and effector present, no latent
    /// traits, and a sparse random wiring. Sparse rather than fully connected
    /// because a dense brain has no room to grow - adding a connection is one of
    /// the mutation operators, and it needs somewhere to go.
    /// </summary>
    public static Genome CreateSeed(ref Core.Pcg32 rng, float connectionDensity = 0.35f)
    {
        var traits = new float[Traits.Count];
        for (int i = 0; i < traits.Length; i++)
            traits[i] = rng.NextFloat(0.3f, 0.7f);

        var genome = new Genome(traits, new List<NodeGene>(), new List<ConnGene>());

        genome.InsertNode(new NodeGene(NodeIds.Bias, NodeKind.Bias, 0f));

        foreach (int id in InnateNodeIds.Sensors())
            genome.InsertNode(new NodeGene(id, NodeKind.Sensor, 0f));

        foreach (int id in InnateNodeIds.Effectors())
            genome.InsertNode(new NodeGene(id, NodeKind.Effector, rng.NextGauss(0f, 0.2f)));

        // Wire sensors to effectors directly. Hidden layers arrive by mutation.
        foreach (int from in InnateNodeIds.SensorsAndBias())
        {
            foreach (int to in InnateNodeIds.Effectors())
            {
                if (rng.Chance(connectionDensity))
                    genome.AddConnection(from, to, rng.NextGauss(0f, 0.8f));
            }
        }

        // A brain with no connections cannot act and cannot be selected on, so
        // guarantee each effector has at least one input.
        foreach (int to in InnateNodeIds.Effectors())
        {
            if (!genome.HasIncoming(to))
            {
                int from = InnateNodeIds.RandomSensorOrBias(ref rng);
                genome.AddConnection(from, to, rng.NextGauss(0f, 0.8f));
            }
        }

        return genome;
    }

    /// <summary>Inserts keeping <see cref="Nodes"/> sorted by id. No-op if present.</summary>
    public void InsertNode(NodeGene node)
    {
        int index = FindNode(node.Id);
        if (index >= 0) return;
        Nodes.Insert(~index, node);
    }

    /// <summary>Index of a node id, or the bitwise complement of its insert point.</summary>
    public int FindNode(int id)
    {
        int lo = 0, hi = Nodes.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int cmp = Nodes[mid].Id.CompareTo(id);
            if (cmp == 0) return mid;
            if (cmp < 0) lo = mid + 1; else hi = mid - 1;
        }
        return ~lo;
    }

    public bool HasNode(int id) => FindNode(id) >= 0;

    /// <summary>
    /// Adds a connection keeping <see cref="Conns"/> sorted by (To, From).
    /// Returns false if the pair already exists or the target cannot receive.
    /// </summary>
    public bool AddConnection(int from, int to, float weight)
    {
        if (!NodeIds.CanReceive(to)) return false;
        if (!HasNode(from) || !HasNode(to)) return false;

        int index = FindConn(from, to);
        if (index >= 0) return false;

        Conns.Insert(~index, new ConnGene(from, to, weight, Enabled: true));
        return true;
    }

    /// <summary>Index of the (from, to) connection, or the complement of its
    /// insert point, under the (To, From) ordering.</summary>
    public int FindConn(int from, int to)
    {
        int lo = 0, hi = Conns.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int cmp = Compare(Conns[mid], to, from);
            if (cmp == 0) return mid;
            if (cmp < 0) lo = mid + 1; else hi = mid - 1;
        }
        return ~lo;

        static int Compare(in ConnGene c, int to, int from)
        {
            int byTo = c.To.CompareTo(to);
            return byTo != 0 ? byTo : c.From.CompareTo(from);
        }
    }

    public bool HasIncoming(int nodeId)
    {
        for (int i = 0; i < Conns.Count; i++)
            if (Conns[i].To == nodeId && Conns[i].Enabled) return true;
        return false;
    }

    public bool HasOutgoing(int nodeId)
    {
        for (int i = 0; i < Conns.Count; i++)
            if (Conns[i].From == nodeId && Conns[i].Enabled) return true;
        return false;
    }

    /// <summary>Enabled connection count - the brain's real complexity, and what
    /// its metabolic cost is charged against.</summary>
    public int EnabledConnCount()
    {
        int n = 0;
        for (int i = 0; i < Conns.Count; i++) if (Conns[i].Enabled) n++;
        return n;
    }
}

/// <summary>The node ids every creature is born with.</summary>
public static class InnateNodeIds
{
    public static IEnumerable<int> Sensors()
    {
        for (int c = 0; c < Enum.GetValues<InnateSense>().Length; c++)
            yield return NodeIds.Sensor(NodeIds.InnateSlot, c);

        for (int c = 0; c < Vision.BinCount * Vision.ChannelsPerBin; c++)
            yield return NodeIds.Sensor(NodeIds.VisionSlot, c);
    }

    public static IEnumerable<int> SensorsAndBias()
    {
        yield return NodeIds.Bias;
        foreach (int id in Sensors()) yield return id;
    }

    public static IEnumerable<int> Effectors()
    {
        for (int c = 0; c < Enum.GetValues<InnateAction>().Length; c++)
            yield return NodeIds.Effector(NodeIds.InnateSlot, c);
    }

    public static int SensorCount =>
        Enum.GetValues<InnateSense>().Length + Vision.BinCount * Vision.ChannelsPerBin;

    public static int RandomSensorOrBias(ref Core.Pcg32 rng)
    {
        int pick = rng.NextInt(SensorCount + 1);
        if (pick == 0) return NodeIds.Bias;

        pick -= 1;
        int innate = Enum.GetValues<InnateSense>().Length;
        return pick < innate
            ? NodeIds.Sensor(NodeIds.InnateSlot, pick)
            : NodeIds.Sensor(NodeIds.VisionSlot, pick - innate);
    }
}
