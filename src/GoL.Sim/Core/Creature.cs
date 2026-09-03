using System;
using System.Numerics;
using GoL.Sim.Brains;
using GoL.Sim.Genetics;

namespace GoL.Sim.Core;

/// <summary>
/// One living agent: a body, a genome, and the brain compiled from it.
/// <para>
/// Uses <see cref="System.Numerics.Vector2"/>, never the XNA type - the whole
/// simulation assembly stays free of the graphics stack so it can be tested and
/// soaked without a window.
/// </para>
/// </summary>
public sealed class Creature
{
    public Creature(int id, Genome genome, Vector2 position, float heading, ulong worldSeed, long birthTick)
    {
        Id = id;
        Genome = genome;
        Brain = Brain.Compile(genome);
        Position = position;
        Heading = heading;
        BirthTick = birthTick;

        // The creature's random stream is a pure function of (worldSeed, id,
        // birthTick), so its sequence does not depend on the order creatures are
        // updated in - which keeps the whole simulation refactor-tolerant.
        Rng = new Pcg32(Pcg32.Mix(worldSeed, (ulong)id, (ulong)birthTick), StreamId.Creature);

        Energy = MaxEnergy * 0.6f;

        ForwardEye = new Eye(Vision.BinCount);
        Layout = new SensorLayout(Brain);
    }

    /// <summary>The eye every creature has. Its bins are both the vision sensor's
    /// input and what the V overlay draws.</summary>
    public Eye ForwardEye { get; }

    /// <summary>Present only once the RearEye trait is unlocked.</summary>
    public Eye? RearEye { get; private set; }

    /// <summary>Node-id to dense-index map for this creature's brain.</summary>
    public SensorLayout Layout { get; private set; }

    public Eye EnsureRearEye() => RearEye ??= new Eye(Vision.RearBinCount);

    /// <summary>
    /// Recompiles the brain and sensor map after the genome changed in place.
    /// <para>
    /// Normally a genome is fixed for a lifetime and this is unnecessary - mutation
    /// happens at birth. The lab uses it to grant an attribute on demand, so the
    /// open-ended trait system is demonstrable without waiting for a rare unlock.
    /// </para>
    /// </summary>
    public void Rebuild()
    {
        Brain.Recompile(Genome);
        Layout = new SensorLayout(Brain);
    }

    /// <summary>Last tick's brain output, kept for the overlays - the mouth arc
    /// flares while biting, and the attribute panel shows the drive values.</summary>
    public Intent LastIntent;

    /// <summary>Whether a bite actually connected this tick, as opposed to the
    /// mouth merely being open. Cleared by the world each step.</summary>
    public bool BitThisTick;

    public int Id { get; }
    public Genome Genome { get; }
    public Brain Brain { get; }
    public long BirthTick { get; }

    public Vector2 Position;
    public float Heading;

    /// <summary>Signed forward speed in units per second.</summary>
    public float Speed;

    /// <summary>Signed turn rate in radians per second.</summary>
    public float AngularVelocity;

    public float Energy;
    public float Age;
    public bool Alive { get; set; } = true;

    /// <summary>Accumulated poison. Drains energy until it decays away.</summary>
    public float ToxinLoad;

    /// <summary>Simulation time of the last birth, for the reproduction cooldown.</summary>
    public float LastBirthTime = float.NegativeInfinity;

    public Pcg32 Rng;

    // --- Derived body measurements, cached because they are read every tick and
    // only change when the genome does (i.e. never, within a lifetime). ---

    public float Radius => Genome.Trait(TraitAxis.BodyRadius);

    public float MaxEnergy => 20f * Mass * 6.5f;

    /// <summary>Body mass, normalized so a mid-sized creature is about 1.</summary>
    public float Mass
    {
        get
        {
            float r = Radius / 8f;
            return r * r;
        }
    }

    public float EnergyFraction => MaxEnergy > 0f ? Math.Clamp(Energy / MaxEnergy, 0f, 1f) : 0f;

    public Vector2 Forward => new(MathF.Cos(Heading), MathF.Sin(Heading));

    /// <summary>Where the nose samples pheromones - just in front of the body.</summary>
    public Vector2 NosePosition => Position + Forward * Radius;

    /// <summary>How far the mouth can reach from the body centre.</summary>
    public float MouthRange => Radius + Genome.Trait(TraitAxis.MouthReach);
}

/// <summary>Stream identifiers keeping the world's random sequences independent.
/// Two streams with the same seed but different ids do not correlate.</summary>
public static class StreamId
{
    public const ulong World = 1;
    public const ulong Plants = 2;
    public const ulong Creature = 3;
    public const ulong Respawn = 4;
}
