using System;
using System.Collections.Generic;
using GoL.Sim.Genetics;

namespace GoL.Sim.Brains;

/// <summary>
/// A genome's network compiled into flat arrays for evaluation.
/// <para>
/// <b>Evaluation is a single pass over the previous tick's activations.</b> Every
/// connection reads its source from the previous buffer and accumulates into the
/// current one; then biases and the activation function are applied and the
/// buffers swap. This makes cycles and self-loops legal <i>by construction</i> -
/// there is no topological sort to get wrong, no cycle detection that has to stay
/// correct across every mutation operator, and no tie-breaking rule that could
/// leak into results.
/// </para>
/// <para>
/// The cost is one tick of propagation delay per layer of depth. At 60Hz and a
/// few effective layers that is tens of milliseconds of reaction latency, which is
/// biologically plausible and imperceptible here.
/// </para>
/// <para>
/// The genome's lists are the storage format; this is the runtime format, built
/// once at birth. Arrays are owned for the object's life and reused on recompile,
/// because this runs for hundreds of creatures every tick and must not allocate.
/// </para>
/// </summary>
public sealed class Brain
{
    // Connections, as dense indices into the node arrays. Sorted by (to, from) -
    // inherited from the genome's ordering, and load-bearing: float addition is
    // not associative, so accumulation order is part of the semantics.
    private int[] _connFrom = Array.Empty<int>();
    private int[] _connTo = Array.Empty<int>();
    private float[] _connWeight = Array.Empty<float>();

    private float[] _bias = Array.Empty<float>();
    private NodeKind[] _kind = Array.Empty<NodeKind>();

    private float[] _prev = Array.Empty<float>();
    private float[] _cur = Array.Empty<float>();

    // Dense index of each sensor / effector node, in genome node-id order.
    private int[] _sensorIndex = Array.Empty<int>();
    private int[] _effectorIndex = Array.Empty<int>();

    // Node id of each sensor / effector, parallel to the arrays above. The world
    // uses these to place values into the right sensor slot.
    private int[] _sensorNodeId = Array.Empty<int>();
    private int[] _effectorNodeId = Array.Empty<int>();

    private int _biasIndex = -1;

    public int NodeCount { get; private set; }
    public int ConnCount { get; private set; }
    public int SensorCount { get; private set; }
    public int EffectorCount { get; private set; }

    public static Brain Compile(Genome genome)
    {
        var brain = new Brain();
        brain.Recompile(genome);
        return brain;
    }

    /// <summary>Rebuilds from a genome, reusing arrays where they are big enough.
    /// Called at birth, including when a pooled creature slot is reused.</summary>
    public void Recompile(Genome genome)
    {
        NodeCount = genome.Nodes.Count;

        EnsureCapacity(ref _bias, NodeCount);
        EnsureCapacity(ref _kind, NodeCount);
        EnsureCapacity(ref _prev, NodeCount);
        EnsureCapacity(ref _cur, NodeCount);

        // Node ids are sorted in the genome, so a node's dense index is just its
        // position, and id -> index is a binary search.
        int sensors = 0, effectors = 0;
        for (int i = 0; i < NodeCount; i++)
        {
            var node = genome.Nodes[i];
            _bias[i] = node.Bias;
            _kind[i] = node.Kind;
            _prev[i] = 0f;
            _cur[i] = 0f;

            switch (node.Kind)
            {
                case NodeKind.Sensor: sensors++; break;
                case NodeKind.Effector: effectors++; break;
                case NodeKind.Bias: _biasIndex = i; break;
            }
        }

        SensorCount = sensors;
        EffectorCount = effectors;
        EnsureCapacity(ref _sensorIndex, sensors);
        EnsureCapacity(ref _sensorNodeId, sensors);
        EnsureCapacity(ref _effectorIndex, effectors);
        EnsureCapacity(ref _effectorNodeId, effectors);

        int si = 0, ei = 0;
        for (int i = 0; i < NodeCount; i++)
        {
            var node = genome.Nodes[i];
            if (node.Kind == NodeKind.Sensor)
            {
                _sensorIndex[si] = i;
                _sensorNodeId[si] = node.Id;
                si++;
            }
            else if (node.Kind == NodeKind.Effector)
            {
                _effectorIndex[ei] = i;
                _effectorNodeId[ei] = node.Id;
                ei++;
            }
        }

        // Disabled connections are dropped here rather than skipped every tick.
        int enabled = genome.EnabledConnCount();
        EnsureCapacity(ref _connFrom, enabled);
        EnsureCapacity(ref _connTo, enabled);
        EnsureCapacity(ref _connWeight, enabled);

        int c = 0;
        for (int i = 0; i < genome.Conns.Count; i++)
        {
            var conn = genome.Conns[i];
            if (!conn.Enabled) continue;

            _connFrom[c] = genome.FindNode(conn.From);
            _connTo[c] = genome.FindNode(conn.To);
            _connWeight[c] = conn.Weight;
            c++;
        }
        ConnCount = c;

        if (_biasIndex >= 0) _prev[_biasIndex] = 1f;
    }

    /// <summary>Node id of the sensor at a dense sensor index.</summary>
    public int SensorNodeId(int sensorIndex) => _sensorNodeId[sensorIndex];

    /// <summary>Node id of the effector at a dense effector index.</summary>
    public int EffectorNodeId(int effectorIndex) => _effectorNodeId[effectorIndex];

    /// <summary>
    /// Runs one tick. <paramref name="sensors"/> and <paramref name="effectors"/>
    /// are indexed densely, in ascending node-id order - use
    /// <see cref="SensorNodeId"/> to build the mapping once at birth.
    /// </summary>
    public void Evaluate(ReadOnlySpan<float> sensors, Span<float> effectors)
    {
        // Sensors are written into the PREVIOUS buffer, which is what the
        // connection pass reads. They are inputs, not computed values.
        for (int i = 0; i < SensorCount; i++)
            _prev[_sensorIndex[i]] = sensors[i];

        if (_biasIndex >= 0) _prev[_biasIndex] = 1f;

        var cur = _cur;
        Array.Clear(cur, 0, NodeCount);

        var from = _connFrom;
        var to = _connTo;
        var weight = _connWeight;
        var prev = _prev;

        for (int i = 0; i < ConnCount; i++)
            cur[to[i]] += prev[from[i]] * weight[i];

        for (int i = 0; i < NodeCount; i++)
        {
            switch (_kind[i])
            {
                case NodeKind.Sensor:
                    cur[i] = prev[i];       // carried through untouched
                    break;
                case NodeKind.Bias:
                    cur[i] = 1f;
                    break;
                default:
                    cur[i] = Activate(cur[i] + _bias[i]);
                    break;
            }
        }

        (_prev, _cur) = (cur, prev);

        for (int i = 0; i < EffectorCount; i++)
            effectors[i] = _prev[_effectorIndex[i]];
    }

    /// <summary>
    /// Softsign. Chosen over tanh because it is one divide with no call into
    /// libm - so it is fast, and bit-identical across runs without depending on a
    /// platform's transcendental implementation. Saturates to (-1, 1), which is
    /// what keeps a runaway recurrent loop bounded instead of producing infinities.
    /// </summary>
    public static float Activate(float x) => x / (1f + MathF.Abs(x));

    /// <summary>Current activation of every node, for the brain-graph overlay.</summary>
    public void CopyActivations(Span<float> destination)
    {
        for (int i = 0; i < NodeCount; i++) destination[i] = _prev[i];
    }

    private static void EnsureCapacity<T>(ref T[] array, int needed)
    {
        if (array.Length < needed) array = new T[Math.Max(needed, array.Length * 2)];
    }
}
