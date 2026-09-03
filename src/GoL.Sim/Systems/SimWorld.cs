using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using MonoGame.Extended.ECS;
using GoL.Sim.Brains;
using GoL.Sim.Components;
using GoL.Sim.Core;
using GoL.Sim.Genetics;
using XnaEntity = MonoGame.Extended.ECS.Entity;

namespace GoL.Sim.Systems;

/// <summary>
/// The simulation. Owns the ECS world, the environment it runs in, and the fixed
/// timestep that drives both.
/// <para>
/// <b>The simulation never sees wall-clock time.</b> <see cref="Step"/> takes no
/// arguments and synthesises a <see cref="GameTime"/> carrying only the configured
/// timestep. The host accumulates real time and calls <c>Step</c> an integer number
/// of times; that is what makes a seeded run reproduce exactly.
/// </para>
/// </summary>
public sealed class SimWorld
{
    private readonly World _ecs;
    private readonly GameTime _fixedStep;
    private readonly List<int> _living = new();

    private Pcg32 _worldRng;

    public SimWorld(SimConfig config, IEnvironment environment)
    {
        Config = config;
        Environment = environment;

        _worldRng = new Pcg32((ulong)(config.Seed == 0 ? 20260903 : config.Seed), StreamId.World);

        _fixedStep = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(config.SecondsPerTick));

        // System order IS the tick order, and it is load-bearing - see
        // docs/ARCHITECTURE.md. Fields first so everyone perceives the same state;
        // sense and think fully before anything acts; lifecycle last so entity
        // creation and destruction never happen mid-iteration.
        // The environment needs to find creatures, but the ECS owns them - so the
        // simulation hands it a bridge rather than the entity store.
        if (environment.SenseField is LabEnvironment lab)
            lab.Creatures = new EcsCreatureIndex(this);

        _ecs = new WorldBuilder()
            .AddSystem(new FieldSystem(this))
            .AddSystem(new SenseSystem(this))
            .AddSystem(new ThinkSystem())
            .AddSystem(new ActuateSystem(this))
            .AddSystem(new FeedSystem(this))
            .AddSystem(new EnergySystem(this))
            .AddSystem(new LifecycleSystem(this))
            .Build();
    }

    public SimConfig Config { get; }

    /// <summary>The plants, fields and spatial index this world runs in.</summary>
    public IEnvironment Environment { get; }

    /// <summary>What creatures can perceive. The environment provides it.</summary>
    public ISenseField Field => Environment.SenseField;

    public float SimTime { get; private set; }
    public long Tick { get; private set; }

    /// <summary>Living creature count, refreshed each tick.</summary>
    public int Population { get; private set; }

    /// <summary>Highest generation seen, for the run stats.</summary>
    public int MaxGeneration { get; private set; }

    /// <summary>Attributes that have ever appeared in this run, and when.</summary>
    public Dictionary<LatentTraitId, float> UnlockTimeline { get; } = new();

    /// <summary>Advances exactly one fixed step.</summary>
    public void Step()
    {
        Tick++;
        SimTime += Config.SecondsPerTick;
        _ecs.Update(_fixedStep);
    }

    // --- entity lifecycle ---

    public int Spawn(Genome genome, Vector2 position, float heading)
    {
        var entity = _ecs.CreateEntity();
        int id = entity.Id;

        var brain = Brain.Compile(genome);
        var mind = new Mind { Brain = brain, Layout = new SensorLayout(brain) };
        mind.EnsureBuffers();

        float radius = genome.Trait(TraitAxis.BodyRadius);
        float mass = (radius / 8f) * (radius / 8f);
        float maxEnergy = 20f * mass * 6.5f;

        entity.Attach(new Body { Position = position, Heading = heading, Radius = radius });
        entity.Attach(new Energy { Current = maxEnergy * 0.6f, Maximum = maxEnergy });
        entity.Attach(new Vitals { BirthTick = Tick });
        entity.Attach(new Genes { Genome = genome });
        entity.Attach(mind);
        entity.Attach(new Sight { Forward = new Eye(Vision.BinCount) });
        entity.Attach(new RandomSource((ulong)Config.Seed, id, Tick));

        RecordUnlocks(genome);
        return id;
    }

    /// <summary>Splits a parent's energy with a mutated offspring.</summary>
    public void Reproduce(int parentId, Body body, Energy energy, Vitals vitals, Genes genes)
    {
        var source = _ecs.GetEntity(parentId).Get<RandomSource>();

        var childGenome = Mutator.Reproduce(genes.Genome, ref source.Rng, Config);

        float share = energy.Current * Metabolism.OffspringShare;
        energy.Current *= 1f - Metabolism.OffspringShare - Metabolism.BirthOverhead;
        vitals.LastBirthTime = SimTime;

        // Placed behind the parent so a newborn is not immediately inside it.
        float childRadius = childGenome.Trait(TraitAxis.BodyRadius);
        var behind = -body.Forward * (body.Radius + childRadius + 1f);
        var position = Field.Wrap(body.Position + behind);

        int childId = Spawn(childGenome, position, body.Heading + MathF.PI);
        _ecs.GetEntity(childId).Get<Energy>().Current = share;
    }

    public void Kill(int id, Body body, Energy energy)
    {
        Environment.OnDeath(body.Position, body.Radius, energy.Current);
        _ecs.DestroyEntity(id);
    }

    // --- services the systems call into ---

    public void StepFields(float dt) => Environment.Step(this, dt);

    public void EmitScent(Vector2 position, in Intent intent, float dt)
    {
        if (intent.EmitScent0 > 0f) Environment.Emit(position, 0, intent.EmitScent0 * dt);
        if (intent.EmitScent1 > 0f) Environment.Emit(position, 1, intent.EmitScent1 * dt);
    }

    public void ResolveBite(Body body, Energy energy, Genome genome, Mind mind, float dt)
        => Environment.ResolveBite(this, body, energy, genome, mind, dt);

    /// <summary>
    /// Every living entity id, ascending. Published by <see cref="LifecycleSystem"/>,
    /// which already walks exactly this set - rather than rediscovered by probing
    /// entity ids, which would cost a scan of the whole id space every tick.
    /// </summary>
    public IReadOnlyList<int> Living => _living;

    /// <summary>Called by <see cref="LifecycleSystem"/> at the end of its pass.</summary>
    internal void PublishLiving(List<int> living, int maxGeneration)
    {
        _living.Clear();
        _living.AddRange(living);
        Population = _living.Count;
        if (maxGeneration > MaxGeneration) MaxGeneration = maxGeneration;
    }

    public XnaEntity GetEntity(int id) => _ecs.GetEntity(id);

    public T Get<T>(int id) where T : class => _ecs.GetEntity(id).Get<T>();

    /// <summary>A read-only handle to one creature, for renderers and tests.</summary>
    public CreatureView View(int id)
    {
        var entity = _ecs.GetEntity(id);
        return new CreatureView(
            id,
            entity.Get<Body>(), entity.Get<Energy>(), entity.Get<Vitals>(),
            entity.Get<Genes>(), entity.Get<Mind>(), entity.Get<Sight>());
    }

    /// <summary>Random draws that belong to the world rather than to a creature -
    /// plant seeding and repopulation. Kept on a stream of its own so adding a
    /// creature never shifts the world's own sequence.</summary>
    public ref Pcg32 WorldRng => ref _worldRng;


    /// <summary>
    /// A fingerprint of the whole simulation state, for regression tests and the
    /// soak runner.
    /// <para>
    /// Floats are quantised before hashing, so the hash is insensitive to
    /// display-only changes but still catches any real divergence. Walks
    /// <see cref="Living"/>, which is ascending by entity id - the order has to be
    /// stable or the hash would differ between runs that are actually identical.
    /// </para>
    /// </summary>
    public ulong Hash()
    {
        const ulong Offset = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;

        ulong hash = Offset;

        void Mix(ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                hash ^= (value >> (i * 8)) & 0xFF;
                hash *= Prime;
            }
        }

        static ulong Quantise(float v) => (ulong)(long)MathF.Round(v * 1024f);

        Mix((ulong)Tick);
        Mix((ulong)_living.Count);

        for (int i = 0; i < _living.Count; i++)
        {
            int id = _living[i];
            var body = Get<Body>(id);
            var energy = Get<Energy>(id);
            var vitals = Get<Vitals>(id);
            var genes = Get<Genes>(id);

            Mix((ulong)id);
            Mix(Quantise(body.Position.X));
            Mix(Quantise(body.Position.Y));
            Mix(Quantise(body.Heading));
            Mix(Quantise(energy.Current));
            Mix(Quantise(vitals.Age));
            Mix(genes.Genome.UnlockedMask);
            Mix((ulong)genes.Genome.EnabledConnCount());
        }

        return hash;
    }

    private void RecordUnlocks(Genome genome)
    {
        if (genome.UnlockedMask == 0) return;

        foreach (var trait in LatentTraitCatalog.All)
        {
            if (!genome.Has(trait.Id)) continue;
            if (UnlockTimeline.ContainsKey(trait.Id)) continue;

            // First appearance in the run. This timeline is the payoff of the whole
            // design and belongs on screen, not in a log.
            UnlockTimeline[trait.Id] = SimTime;
        }
    }
}
