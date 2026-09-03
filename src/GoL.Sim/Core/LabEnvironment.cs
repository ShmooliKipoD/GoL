using System;
using System.Collections.Generic;
using GoL.Sim.Components;
using GoL.Sim.Genetics;
using GoL.Sim.Systems;

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
/// A minimal environment for the Creature Lab: a few hand-placed plants,
/// brute-force queries, and a pheromone grid that decays but does not diffuse.
/// <para>
/// <b>Deliberately throwaway.</b> The board environment replaces it behind the same
/// <see cref="IEnvironment"/> and <see cref="ISenseField"/>. If that swap requires
/// changing a system, the seam was drawn in the wrong place.
/// </para>
/// </summary>
public sealed class LabEnvironment : IEnvironment, ISenseField
{
    private const int ScentChannels = 2;
    private const int ScentGrid = 32;

    private readonly float[] _scent = new float[ScentChannels * ScentGrid * ScentGrid];
    private readonly List<LabPlant> _plants = new();
    private Pcg32 _rng;
    private int _nextPlantId;

    public LabEnvironment(SimConfig config)
    {
        // A small arena: the lab is for inspecting one creature, so the view can be
        // scaled up enough to see its eyes and mouth arc.
        WorldSize = MathF.Min(config.WorldSize, 340f);
        Toroidal = config.Toroidal;
        _rng = new Pcg32((ulong)(config.Seed == 0 ? 12345 : config.Seed), StreamId.Plants);
    }

    public ISenseField SenseField => this;

    public float WorldSize { get; }
    public bool Toroidal { get; }

    public IReadOnlyList<LabPlant> Plants => _plants;

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

    public void SeedPlants(int count)
    {
        for (int i = 0; i < count; i++)
            AddPlant(new Vector2(_rng.NextFloat(0f, WorldSize), _rng.NextFloat(0f, WorldSize)));
    }

    // --- IEnvironment ---

    public void Step(SimWorld world, float dt)
    {
        DecayScent(dt);
        foreach (var plant in _plants)
            plant.Energy = MathF.Min(plant.MaxEnergy, plant.Energy + 1.2f * dt);
    }

    public void ResolveBite(
        SimWorld world, Body body, Energy energy, Genome genome, Mind mind, float dt)
    {
        float reach = body.Radius + genome.Trait(TraitAxis.MouthReach);
        float arc = genome.Trait(TraitAxis.MouthArc);

        LabPlant? best = null;
        float bestDistance = float.MaxValue;

        foreach (var plant in _plants)
        {
            if (!plant.Alive) continue;

            var offset = Offset(body.Position, plant.Position);
            float distance = offset.Length() - plant.Radius;
            if (distance > reach) continue;

            float relative = Senses.WrapAngle(MathF.Atan2(offset.Y, offset.X) - body.Heading);
            if (MathF.Abs(relative) > arc) continue;

            if (distance < bestDistance) { bestDistance = distance; best = plant; }
        }

        if (best is null) return;

        float take = MathF.Min(Metabolism.BiteRate * dt, best.Energy);
        best.Energy -= take;
        energy.Current = MathF.Min(
            energy.Maximum, energy.Current + take * genome.Trait(TraitAxis.DigestGrass));
        mind.BitThisTick = true;
    }

    /// <summary>The lab leaves no corpse; carrion is a board concern.</summary>
    public void OnDeath(Vector2 position, float radius, float remainingEnergy) { }

    // --- ISenseField ---

    public int Query(Vector2 centre, float radius, int excludeCreatureId, Span<Percept> results)
    {
        int count = 0;

        // Plants in id order - a stable order, so any float accumulated over these
        // results is reproducible.
        foreach (var plant in _plants)
        {
            if (!plant.Alive) continue;
            if (count >= results.Length) return count;

            var offset = Offset(centre, plant.Position);
            float reach = radius + plant.Radius;
            if (offset.LengthSquared() > reach * reach) continue;

            results[count++] = new Percept(plant.Position, plant.Radius, SeenKind.Plant, 0f, plant.Id);
        }

        count += Creatures.Query(centre, radius, excludeCreatureId, results[count..]);
        return count;
    }

    /// <summary>How creatures are found. Set by the simulation at start-up, because
    /// the environment does not own the entities - the ECS does.</summary>
    public ICreatureIndex Creatures { get; set; } = EmptyCreatureIndex.Instance;

    public float SampleScent(Vector2 position, int channel)
    {
        if (channel < 0 || channel >= ScentChannels) return 0f;
        var (x, y) = Cell(position);
        return _scent[Index(channel, x, y)];
    }

    /// <summary>Flat in the lab. A real fertility field is a board feature.</summary>
    public float SampleFertility(Vector2 position) => 0.5f;

    public Vector2 Offset(Vector2 from, Vector2 to)
    {
        var delta = to - from;
        if (!Toroidal) return delta;

        // The shorter way round, or creatures go blind at the world seam.
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

    public void Emit(Vector2 position, int channel, float amount)
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
}

/// <summary>
/// How an environment finds creatures. The ECS owns the entities, so the
/// environment cannot enumerate them itself - the simulation supplies this.
/// </summary>
public interface ICreatureIndex
{
    int Query(Vector2 centre, float radius, int excludeCreatureId, Span<Percept> results);
}

internal sealed class EmptyCreatureIndex : ICreatureIndex
{
    public static readonly EmptyCreatureIndex Instance = new();
    public int Query(Vector2 c, float r, int exclude, Span<Percept> results) => 0;
}
