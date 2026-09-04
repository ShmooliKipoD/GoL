using System;
using System.Numerics;
using GoL.Sim.Brains;
using GoL.Sim.Core;
using GoL.Sim.Genetics;

namespace GoL.Sim.Components;

/// <summary>
/// Components are <b>reference types</b> throughout. That is not a style choice:
/// MonoGame.Extended's <c>ComponentMapper&lt;T&gt;</c> constrains <c>T : class</c>.
/// The per-tick path stays allocation-free; birth allocates one object per
/// component, which is the trade this ECS imposes.
/// </summary>
public sealed class Body
{
    public Vector2 Position;
    public float Heading;

    /// <summary>Signed forward speed, units per second.</summary>
    public float Speed;

    /// <summary>Signed turn rate, radians per second.</summary>
    public float AngularVelocity;

    public float Radius;

    public Vector2 Forward => new(MathF.Cos(Heading), MathF.Sin(Heading));

    /// <summary>Body mass, normalized so a mid-sized creature is about 1.</summary>
    public float Mass
    {
        get { float r = Radius / 8f; return r * r; }
    }
}

/// <summary>Stored energy and what it is worth.</summary>
public sealed class Energy
{
    public float Current;
    public float Maximum;

    public float Fraction => Maximum > 0f ? Math.Clamp(Current / Maximum, 0f, 1f) : 0f;
}

/// <summary>Age, poison and breeding history - everything that tracks a life.</summary>
public sealed class Vitals
{
    public float Age;
    public float ToxinLoad;
    public float LastBirthTime = float.NegativeInfinity;
    public bool Alive = true;

    /// <summary>Tick of the most recent birth, or -1 for never.
    /// <para>
    /// A tick counter rather than a comparison against <see cref="LastBirthTime"/>:
    /// that is a float, and "did this happen this tick" written as float equality is
    /// exact today but silently becomes "never" the moment simulation time
    /// accumulates differently. A tick count is exact by construction.
    /// </para></summary>
    public long LastBirthTick = -1;

    /// <summary>Simulation tick this creature was born on. Part of the key for its
    /// random stream, so slot reuse cannot make two creatures share a sequence.</summary>
    public long BirthTick;
}

/// <summary>The heritable design. Everything else about a creature derives from it.</summary>
public sealed class Genes
{
    public required Genome Genome { get; init; }

    public float Trait(TraitAxis axis) => Genome.Trait(axis);
    public bool Has(LatentTraitId id) => Genome.Has(id);
}

/// <summary>The compiled brain, its node-id map, and its working buffers.</summary>
public sealed class Mind
{
    public required Brain Brain { get; set; }
    public required SensorLayout Layout { get; set; }

    /// <summary>
    /// This creature's own sensor and effector vectors.
    /// <para>
    /// Per-creature rather than a shared scratch buffer, because sensing and
    /// thinking are two separate passes over every entity: a shared buffer would
    /// hold only the last creature's readings by the time thinking began. Sized at
    /// birth and on rebuild, so the tick itself never allocates.
    /// </para>
    /// </summary>
    public float[] Sensors { get; private set; } = Array.Empty<float>();
    public float[] Effectors { get; private set; } = Array.Empty<float>();

    /// <summary>What the brain decided last tick. Read by the actuation system and
    /// by the overlays - the mouth band flares from this.</summary>
    public Intent Intent;

    /// <summary>Whether a bite actually connected, as opposed to the mouth merely
    /// being open. Cleared at the start of each tick.</summary>
    public bool BitThisTick;

    /// <summary>Recompiles after the genome changed in place. Normally a genome is
    /// fixed for a lifetime; the lab uses this to grant an attribute on demand.</summary>
    public void Rebuild(Genome genome)
    {
        Brain.Recompile(genome);
        Layout = new SensorLayout(Brain);
        EnsureBuffers();
    }

    /// <summary>Grows the working buffers to fit the brain. Buffers only ever grow,
    /// so a recycled entity slot reuses them.</summary>
    public void EnsureBuffers()
    {
        if (Sensors.Length < Brain.SensorCount) Sensors = new float[Brain.SensorCount];
        if (Effectors.Length < Brain.EffectorCount) Effectors = new float[Brain.EffectorCount];
    }
}

/// <summary>
/// What this creature is doing, as a readout over its effectors.
/// <para>
/// Derived presentation state, kept off <see cref="Mind"/> so that component does
/// not become a bag of unrelated fields. Written only by
/// <c>ActionSystem</c> and read by renderers, the soak runner and tests - nothing
/// in the simulation ever reads it back, or the readout would start driving
/// behaviour instead of describing it.
/// </para>
/// </summary>
public sealed class Behaviour
{
    /// <summary>Every action's value this tick, indexed by <see cref="CreatureAction"/>.
    /// Signed for Move and Turn; zero for anything this genome cannot do.</summary>
    public readonly float[] Values = new float[Actions.Count];

    /// <summary>The action with the greatest magnitude. Only meaningful when
    /// <see cref="Active"/>.</summary>
    public CreatureAction Current;

    /// <summary>False when nothing cleared the deadband - the idle state.</summary>
    public bool Active;

    /// <summary>Actions this genome can perform, as a bitmask over
    /// <see cref="CreatureAction"/>.</summary>
    public int AvailableMask;

    /// <summary>A bite connected this tick, as opposed to the mouth merely opening.</summary>
    public bool Fed;

    /// <summary>A birth happened this tick.</summary>
    public bool Bred;

    public float Value(CreatureAction action) => Values[(int)action];

    public bool Can(CreatureAction action) => (AvailableMask & (1 << (int)action)) != 0;

    /// <summary>What to call what it is doing right now.</summary>
    public string Label() => Active
        ? Actions.Label(Current, Values[(int)Current], Fed, Bred)
        : Actions.IdleLabel;
}

/// <summary>The eyes, and what they currently see.</summary>
public sealed class Sight
{
    public required Eye Forward { get; init; }

    /// <summary>Present only once the RearEye attribute is unlocked.</summary>
    public Eye? Rear { get; set; }

    public Eye EnsureRear() => Rear ??= new Eye(Vision.RearBinCount);
}

/// <summary>
/// This creature's random stream. Seeded from (worldSeed, entityId, birthTick), so
/// its sequence is a pure function of its identity and does not depend on the order
/// creatures happen to be updated in.
/// </summary>
public sealed class RandomSource
{
    public Pcg32 Rng;

    public RandomSource(ulong worldSeed, int entityId, long birthTick)
        => Rng = new Pcg32(Pcg32.Mix(worldSeed, (ulong)entityId, (ulong)birthTick), StreamId.Creature);
}
