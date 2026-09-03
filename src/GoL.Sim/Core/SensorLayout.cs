using System;
using GoL.Sim.Brains;
using GoL.Sim.Genetics;

namespace GoL.Sim.Core;

/// <summary>
/// Maps brain node ids to dense sensor/effector indices for one genome.
/// <para>
/// Built once at birth and kept alongside the brain. The alternative - searching
/// the brain's node list every time a sensor is written - would turn each tick's
/// sensing into a few hundred binary searches per creature.
/// </para>
/// </summary>
public sealed class SensorLayout
{
    private readonly int[] _sensorNodeIds;
    private readonly int[] _effectorNodeIds;

    public SensorLayout(Brain brain)
    {
        _sensorNodeIds = new int[brain.SensorCount];
        for (int i = 0; i < brain.SensorCount; i++)
            _sensorNodeIds[i] = brain.SensorNodeId(i);

        _effectorNodeIds = new int[brain.EffectorCount];
        for (int i = 0; i < brain.EffectorCount; i++)
            _effectorNodeIds[i] = brain.EffectorNodeId(i);
    }

    public int SensorCount => _sensorNodeIds.Length;
    public int EffectorCount => _effectorNodeIds.Length;

    /// <summary>Dense sensor index for a node id, or -1 if this genome lacks it.
    /// Returning -1 rather than throwing is deliberate: a creature without a trait
    /// simply has nowhere to put that trait's reading.</summary>
    public int IndexOf(int sensorNodeId) => BinarySearch(_sensorNodeIds, sensorNodeId);

    /// <summary>Dense effector index for a node id, or -1.</summary>
    public int EffectorIndexOf(int effectorNodeId) => BinarySearch(_effectorNodeIds, effectorNodeId);

    /// <summary>Convenience for the innate effectors every creature has.</summary>
    public int Action(InnateAction action) =>
        EffectorIndexOf(NodeIds.Effector(NodeIds.InnateSlot, (int)action));

    private static int BinarySearch(int[] sorted, int value)
    {
        int lo = 0, hi = sorted.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int cmp = sorted[mid].CompareTo(value);
            if (cmp == 0) return mid;
            if (cmp < 0) lo = mid + 1; else hi = mid - 1;
        }
        return -1;
    }
}
