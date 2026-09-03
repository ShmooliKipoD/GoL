using System;
using System.Collections.Generic;
using System.Numerics;
using GoL.Sim.Genetics;

namespace GoL.Sim.Core;

/// <summary>A morsel of food in the lab.</summary>
public sealed class LabPlant
{
    public int Id;
    public Vector2 Position;
    public float Radius = 4f;
    public float Energy = 12f;
    public float MaxEnergy = 12f;
    public bool Alive => Energy > 0.05f;
}

/// <summary>
/// A minimal world for the Creature Lab: a few hand-placed plants, brute-force
/// queries, and a pheromone field that emits and decays but does not diffuse.
/// <para>
/// <b>This is deliberately throwaway.</b> Step 3 replaces it with the real
/// implementation - spatial hash, growing and spreading plants, diffusing
/// pheromones - behind the same <see cref="ISenseField"/>. If that swap requires
/// touching creature code, the interface was drawn in the wrong place and the
/// interface is what should change.
/// </para>
/// </summary>
public sealed class LabWorld : ISenseField
{
    private const int ScentChannels = 2;
    private const int ScentGrid = 32;

    private readonly float[] _scent = new float[ScentChannels * ScentGrid * ScentGrid];
    private readonly List<LabPlant> _plants = new();
    private Pcg32 _rng;

    public LabWorld(SimConfig config)
    {
        Config = config;
        // A deliberately small arena. The lab is for inspecting one creature, so
        // the view can be scaled up enough to actually see its eyes and mouth arc -
        // at full world scale a creature is about ten pixels across.
        WorldSize = MathF.Min(config.WorldSize, 340f);
        Toroidal = config.Toroidal;
        _rng = new Pcg32((ulong)(config.Seed == 0 ? 12345 : config.Seed), StreamId.Plants);
    }

    public SimConfig Config { get; }
    public float WorldSize { get; }
    public bool Toroidal { get; }

    public IReadOnlyList<LabPlant> Plants => _plants;
    public IReadOnlyList<Creature> Creatures => _creatures;
    private readonly List<Creature> _creatures = new();

    public float SimTime { get; private set; }
    public long Tick { get; private set; }

    private readonly Senses _senses = new();
    private float[] _sensorScratch = new float[512];
    private float[] _effectorScratch = new float[128];
    private int _nextPlantId;
    private int _nextCreatureId;

    public void Add(Creature creature) => _creatures.Add(creature);

    public Creature Spawn(Genome genome, Vector2 position, float heading)
    {
        var creature = new Creature(_nextCreatureId++, genome, position, heading,
            (ulong)Config.Seed, Tick);
        _creatures.Add(creature);
        return creature;
    }

    public LabPlant AddPlant(Vector2 position, float energy = 12f)
    {
        var plant = new LabPlant
        {
            Id = _nextPlantId++,
            Position = position,
            Energy = energy,
            MaxEnergy = energy,
        };
        _plants.Add(plant);
        return plant;
    }

    /// <summary>Scatters food across the arena.</summary>
    public void SeedPlants(int count)
    {
        for (int i = 0; i < count; i++)
        {
            AddPlant(new Vector2(
                _rng.NextFloat(0f, WorldSize),
                _rng.NextFloat(0f, WorldSize)));
        }
    }

    /// <summary>
    /// One fixed step. The phase order matters and is the same order the real world
    /// will use: everything senses, then everything thinks, then everything acts.
    /// </summary>
    public void Step(float dt)
    {
        Tick++;
        SimTime += dt;

        DecayScent(dt);

        for (int i = 0; i < _creatures.Count; i++)
        {
            var creature = _creatures[i];
            if (!creature.Alive) continue;

            creature.BitThisTick = false;
            EnsureScratch(creature);

            var sensors = _sensorScratch.AsSpan(0, creature.Brain.SensorCount);
            var effectors = _effectorScratch.AsSpan(0, creature.Brain.EffectorCount);

            _senses.Sample(creature, this, creature.Layout, SimTime, sensors);
            creature.Brain.Evaluate(sensors, effectors);

            var intent = Locomotion.ReadIntent(creature, effectors);
            creature.LastIntent = intent;

            Locomotion.Apply(creature, intent, this, dt);

            if (intent.Bite) Bite(creature, dt);
            EmitScent(creature, intent, dt);

            Locomotion.Tick(creature, Config.BaseMetabolicRate, dt);
        }

        RegrowPlants(dt);
    }

    private void Bite(Creature creature, float dt)
    {
        float reach = creature.MouthRange;
        float arc = creature.Genome.Trait(TraitAxis.MouthArc);

        LabPlant? best = null;
        float bestDistance = float.MaxValue;

        foreach (var plant in _plants)
        {
            if (!plant.Alive) continue;

            var offset = Offset(creature.Position, plant.Position);
            float distance = offset.Length() - plant.Radius;
            if (distance > reach) continue;

            float relative = Senses.WrapAngle(
                MathF.Atan2(offset.Y, offset.X) - creature.Heading);
            if (MathF.Abs(relative) > arc) continue;

            if (distance < bestDistance) { bestDistance = distance; best = plant; }
        }

        if (best is null) return;

        float take = MathF.Min(Metabolism.BiteRate * dt, best.Energy);
        float efficiency = creature.Genome.Trait(TraitAxis.DigestGrass);

        best.Energy -= take;
        creature.Energy = MathF.Min(creature.MaxEnergy, creature.Energy + take * efficiency);
        creature.BitThisTick = true;
    }

    private void EmitScent(Creature creature, in Intent intent, float dt)
    {
        if (intent.EmitScent0 > 0f) Deposit(creature.Position, 0, intent.EmitScent0 * dt);
        if (intent.EmitScent1 > 0f) Deposit(creature.Position, 1, intent.EmitScent1 * dt);
    }

    private void RegrowPlants(float dt)
    {
        foreach (var plant in _plants)
            plant.Energy = MathF.Min(plant.MaxEnergy, plant.Energy + 1.2f * dt);
    }

    // --- ISenseField ---

    public int Query(Vector2 centre, float radius, int excludeCreatureId, Span<Percept> results)
    {
        int count = 0;
        float radiusSq = radius * radius;

        // Plants first, then creatures, each in id order - a stable order, so any
        // float accumulated over these results is reproducible.
        foreach (var plant in _plants)
        {
            if (!plant.Alive) continue;
            if (count >= results.Length) return count;

            var offset = Offset(centre, plant.Position);
            float reach = radius + plant.Radius;
            if (offset.LengthSquared() > reach * reach) continue;

            results[count++] = new Percept(plant.Position, plant.Radius, SeenKind.Plant, 0f, plant.Id);
        }

        foreach (var other in _creatures)
        {
            if (!other.Alive || other.Id == excludeCreatureId) continue;
            if (count >= results.Length) return count;

            var offset = Offset(centre, other.Position);
            float reach = radius + other.Radius;
            if (offset.LengthSquared() > reach * reach) continue;

            results[count++] = new Percept(
                other.Position, other.Radius, SeenKind.Creature,
                other.Genome.Normalized(TraitAxis.Hue), other.Id);
        }

        return count;
    }

    public float SampleScent(Vector2 position, int channel)
    {
        if (channel < 0 || channel >= ScentChannels) return 0f;
        var (x, y) = Cell(position);
        return _scent[Index(channel, x, y)];
    }

    /// <summary>Flat in the lab. The real fertility field arrives with Step 3.</summary>
    public float SampleFertility(Vector2 position) => 0.5f;

    public Vector2 Offset(Vector2 from, Vector2 to)
    {
        var delta = to - from;
        if (!Toroidal) return delta;

        // Take the shorter way round, or creatures go blind at the world seam.
        float half = WorldSize * 0.5f;
        if (delta.X > half) delta.X -= WorldSize;
        else if (delta.X < -half) delta.X += WorldSize;
        if (delta.Y > half) delta.Y -= WorldSize;
        else if (delta.Y < -half) delta.Y += WorldSize;
        return delta;
    }

    public Vector2 Wrap(Vector2 position)
    {
        if (!Toroidal)
        {
            return new Vector2(
                Math.Clamp(position.X, 0f, WorldSize),
                Math.Clamp(position.Y, 0f, WorldSize));
        }

        float x = position.X % WorldSize;
        float y = position.Y % WorldSize;
        if (x < 0f) x += WorldSize;
        if (y < 0f) y += WorldSize;
        return new Vector2(x, y);
    }

    // --- scent field ---

    public void Deposit(Vector2 position, int channel, float amount)
    {
        if (channel < 0 || channel >= ScentChannels) return;
        var (x, y) = Cell(position);
        int i = Index(channel, x, y);
        _scent[i] = MathF.Min(1f, _scent[i] + amount);
    }

    private void DecayScent(float dt)
    {
        float keep = MathF.Max(0f, 1f - 0.35f * dt);
        for (int i = 0; i < _scent.Length; i++)
        {
            _scent[i] *= keep;
            if (_scent[i] < 1e-4f) _scent[i] = 0f;   // denormals would crawl
        }
    }

    public float ScentAt(int channel, int x, int y) => _scent[Index(channel, x, y)];
    public int ScentResolution => ScentGrid;
    public float ScentCellSize => WorldSize / ScentGrid;

    private (int X, int Y) Cell(Vector2 position)
    {
        var wrapped = Wrap(position);
        int x = Math.Clamp((int)(wrapped.X / WorldSize * ScentGrid), 0, ScentGrid - 1);
        int y = Math.Clamp((int)(wrapped.Y / WorldSize * ScentGrid), 0, ScentGrid - 1);
        return (x, y);
    }

    private static int Index(int channel, int x, int y) =>
        channel * ScentGrid * ScentGrid + y * ScentGrid + x;

    private void EnsureScratch(Creature creature)
    {
        if (_sensorScratch.Length < creature.Brain.SensorCount)
            _sensorScratch = new float[creature.Brain.SensorCount * 2];
        if (_effectorScratch.Length < creature.Brain.EffectorCount)
            _effectorScratch = new float[creature.Brain.EffectorCount * 2];
    }
}
